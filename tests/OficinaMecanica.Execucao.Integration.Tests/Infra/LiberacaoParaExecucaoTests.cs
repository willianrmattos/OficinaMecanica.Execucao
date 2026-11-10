using MongoDB.Bson;
using OficinaMecanica.Contracts.Commands;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Infra;

[Trait("Category", "Infra")]
[Collection(Colecoes.Infra)]
public class LiberacaoParaExecucaoTests(InfraFixture infra)
{
    [Fact(Timeout = 300_000)]
    public async Task IniciarExecucao_DeveLiberarASubstituirOSnapshotEGravarLiberadaEm()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
        var ordem = new OrdemDeProducaoBuilder().DiagnosticoConcluido();
        await ambiente.SemearAsync(ordem);
        var snapshot = new IniciarExecucaoBuilder().ComOrdemDeServicoId(ordem.Id)
            .ComServicos(new ItemDeServicoMensagemBuilder().ComNome("Alinhamento").Build()).Build();

        await ambiente.Os.EnviarAsync(snapshot);

        (await Esperar.AteAsync(async () => (await ambiente.DocumentoDaOrdemAsync(ordem.Id))!["status"].AsString == "AguardandoExecucao"))
            .Should().BeTrue();
        var documento = await ambiente.DocumentoDaOrdemAsync(ordem.Id);
        documento!["liberadaEm"].BsonType.Should().Be(BsonType.DateTime);
        documento["servicos"].AsBsonArray.Single()["nome"].AsString.Should().Be("Alinhamento");
        documento["version"].AsInt64.Should().Be(1);
    }

    [Fact(Timeout = 300_000)]
    public async Task IniciarExecucaoReentregue_DeveSerIdempotenteSemAlterarLiberadaEmNemOSnapshot()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
        var ordem = new OrdemDeProducaoBuilder().DiagnosticoConcluido();
        await ambiente.SemearAsync(ordem);
        var primeira = new IniciarExecucaoBuilder().ComOrdemDeServicoId(ordem.Id)
            .ComServicos(new ItemDeServicoMensagemBuilder().ComNome("Servico aprovado").Build()).Build();
        await ambiente.Os.EnviarAsync(primeira);
        (await Esperar.AteAsync(async () => (await ambiente.DocumentoDaOrdemAsync(ordem.Id))!["status"].AsString == "AguardandoExecucao"))
            .Should().BeTrue();
        var antes = await ambiente.DocumentoDaOrdemAsync(ordem.Id);

        // Mesma mensagem reentregue e outra mensagem (outro MessageId) com snapshot diferente.
        var outroSnapshot = new IniciarExecucaoBuilder().ComOrdemDeServicoId(ordem.Id)
            .ComServicos(new ItemDeServicoMensagemBuilder().ComNome("Servico que nao vale").Build()).Build();
        await ambiente.Os.EnviarAsync(primeira);
        await ambiente.Os.EnviarAsync(outroSnapshot);
        (await Esperar.AteAsync(async () =>
                await ambiente.Api.Banco.GetCollection<BsonDocument>("inbox.states")
                    .CountDocumentsAsync(new BsonDocument("Consumed", new BsonDocument("$ne", BsonNull.Value))) == 2))
            .Should().BeTrue();

        var depois = await ambiente.DocumentoDaOrdemAsync(ordem.Id);
        depois!["liberadaEm"].Should().Be(antes!["liberadaEm"]);
        depois["servicos"].AsBsonArray.Single()["nome"].AsString.Should().Be("Servico aprovado");
        depois["version"].AsInt64.Should().Be(1, "a reentrega nao grava nada");
        depois["status"].AsString.Should().Be("AguardandoExecucao");
        ambiente.Os.Gravador.Falhas.Should().Be(0);
    }

    [Fact(Timeout = 300_000)]
    public async Task IniciarExecucaoDeOrdemJaEmReparo_DeveSerNoOpSemFalha()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
        var ordem = new OrdemDeProducaoBuilder().EmExecucao();
        await ambiente.SemearAsync(ordem);

        await ambiente.Os.EnviarAsync(new IniciarExecucaoBuilder().ComOrdemDeServicoId(ordem.Id).Build());
        (await Esperar.AteAsync(async () =>
                await ambiente.Api.Banco.GetCollection<BsonDocument>("inbox.states")
                    .CountDocumentsAsync(new BsonDocument("Consumed", new BsonDocument("$ne", BsonNull.Value))) == 1))
            .Should().BeTrue();

        (await ambiente.DocumentoDaOrdemAsync(ordem.Id))!["status"].AsString.Should().Be("EmExecucao");
        ambiente.Os.Gravador.Falhas.Should().Be(0);
    }

    [Fact(Timeout = 300_000)]
    public async Task IniciarExecucaoAntesDoDiagnosticoConcluir_DeveIrParaAFilaDeErroSemRetentarRegraDeNegocio()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
        var ordem = new OrdemDeProducaoBuilder().EmDiagnostico();
        await ambiente.SemearAsync(ordem);

        await ambiente.Os.EnviarAsync(new IniciarExecucaoBuilder().ComOrdemDeServicoId(ordem.Id).Build());

        // DomainException esta na lista de excecoes sem retry: a falha chega a saga como Fault.
        (await Esperar.AteAsync(() => ambiente.Os.Gravador.Falhas == 1)).Should().BeTrue();
        (await ambiente.DocumentoDaOrdemAsync(ordem.Id))!["status"].AsString.Should().Be("EmDiagnostico");
    }
}
