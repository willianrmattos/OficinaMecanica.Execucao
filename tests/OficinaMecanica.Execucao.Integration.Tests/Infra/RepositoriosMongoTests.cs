using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Infrastructure.Persistence;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Infra;

[Trait("Category", "Infra")]
[Collection(Colecoes.Infra)]
public class RepositoriosMongoTests(InfraFixture infra) : IAsyncLifetime
{
    private static readonly DateTimeOffset Base = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private AmbienteDeInfra _ambiente = null!;
    private IServiceScope _escopo = null!;

    private IOrdemDeProducaoRepository Ordens => _escopo.ServiceProvider.GetRequiredService<IOrdemDeProducaoRepository>();
    private ICancelamentoPrevioRepository Tombstones => _escopo.ServiceProvider.GetRequiredService<ICancelamentoPrevioRepository>();

    public async Task InitializeAsync()
    {
        _ambiente = await AmbienteDeInfra.CriarAsync(infra);
        _escopo = _ambiente.Api.Services.CreateScope();
    }

    public async Task DisposeAsync()
    {
        _escopo.Dispose();
        await _ambiente.DisposeAsync();
    }

    [Fact(Timeout = 300_000)]
    public async Task AdicionarAsync_NaSegundaVezComOMesmoId_DeveDevolverFalseSemAlterarOQueExiste()
    {
        var ordem = new OrdemDeProducaoBuilder().ComNumero("OS-UNICA-1").NaFila();
        (await Ordens.AdicionarAsync(ordem)).Should().BeTrue();

        var repetida = new OrdemDeProducaoBuilder().ComId(ordem.Id).ComNumero("OS-OUTRA").NaFila();
        (await Ordens.AdicionarAsync(repetida)).Should().BeFalse();

        (await Ordens.ObterPorIdAsync(ordem.Id))!.Numero.Should().Be("OS-UNICA-1");
    }

    [Fact(Timeout = 300_000)]
    public async Task AdicionarAsync_ComNumeroJaUsadoPorOutraOrdem_DeveLancarEmVezDeDevolverFalse()
    {
        await Ordens.AdicionarAsync(new OrdemDeProducaoBuilder().ComNumero("OS-REPETIDA").NaFila());

        var acao = () => Ordens.AdicionarAsync(new OrdemDeProducaoBuilder().ComNumero("OS-REPETIDA").NaFila());

        (await acao.Should().ThrowAsync<MongoWriteException>()).Which.WriteError.Category.Should().Be(ServerErrorCategory.DuplicateKey);
    }

    [Fact(Timeout = 300_000)]
    public async Task AdicionarAsync_ComTombstoneNoMesmoId_DeveDevolverFalseEManterOTombstone()
    {
        var id = Guid.NewGuid();
        (await Tombstones.AdicionarAsync(new CancelamentoPrevioBuilder().DaOrdemDeServico(id).Build())).Should().BeTrue();

        (await Ordens.AdicionarAsync(new OrdemDeProducaoBuilder().ComId(id).NaFila())).Should().BeFalse();

        (await _ambiente.DocumentoDaOrdemAsync(id))!["_t"].AsString.Should().Be("cancelamentoPrevio");
    }

    [Fact(Timeout = 300_000)]
    public async Task Tombstone_NaoDeveSerVistoComoOrdemNemEntrarEmListagensContagensEFilas()
    {
        var tombstone = new CancelamentoPrevioBuilder().Build();
        await Tombstones.AdicionarAsync(tombstone);
        var ordem = new OrdemDeProducaoBuilder().NaFila();
        await Ordens.AdicionarAsync(ordem);

        (await Ordens.ObterPorIdAsync(tombstone.OrdemDeServicoId)).Should().BeNull();
        (await Ordens.ContarAsync(null, null)).Should().Be(1);
        (await Ordens.ListarAsync(null, null, 1, 10)).Select(o => o.Id).Should().Equal(ordem.Id);
        (await Ordens.ContarPorStatusAsync()).Should().BeEquivalentTo(new Dictionary<StatusProducao, int> { [StatusProducao.Fila] = 1 });
        (await Ordens.ObterProximaDaFilaAsync(StatusProducao.Fila))!.Id.Should().Be(ordem.Id);
    }

    [Fact(Timeout = 300_000)]
    public async Task ObterAsyncDoTombstone_NaoDeveEnxergarOrdemComOMesmoId()
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();
        await Ordens.AdicionarAsync(ordem);

        (await Tombstones.ObterAsync(ordem.Id)).Should().BeNull();
    }

    [Fact(Timeout = 300_000)]
    public async Task AdicionarTombstone_ComOrdemOcupandoOId_DeveLancarConcorrenciaEmVezDeDescartarOCancelamento()
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();
        await Ordens.AdicionarAsync(ordem);

        var acao = () => Tombstones.AdicionarAsync(new CancelamentoPrevioBuilder().DaOrdemDeServico(ordem.Id).Build());

        await acao.Should().ThrowAsync<ConcorrenciaException>();
    }

    [Fact(Timeout = 300_000)]
    public async Task AdicionarTombstone_Repetido_DeveDevolverFalseNaSegunda()
    {
        var id = Guid.NewGuid();
        (await Tombstones.AdicionarAsync(new CancelamentoPrevioBuilder().DaOrdemDeServico(id).Build())).Should().BeTrue();

        (await Tombstones.AdicionarAsync(new CancelamentoPrevioBuilder().DaOrdemDeServico(id).Build())).Should().BeFalse();
        (await Tombstones.ObterAsync(id))!.OrdemDeServicoId.Should().Be(id);
    }

    [Fact(Timeout = 300_000)]
    public async Task DentroDeTransacao_ColisaoDeIdNaoDevolveFalse_DeveLancarEAbortar()
    {
        var existente = new OrdemDeProducaoBuilder().NaFila();
        await Ordens.AdicionarAsync(existente);
        var transacao = _escopo.ServiceProvider.GetRequiredService<ITransacaoMongo>();

        await transacao.IniciarAsync(CancellationToken.None);
        try
        {
            var acao = () => Ordens.AdicionarAsync(new OrdemDeProducaoBuilder().ComId(existente.Id).NaFila());

            await acao.Should().ThrowAsync<MongoWriteException>();
        }
        finally
        {
            await transacao.AbortarAsync();
        }
    }

    [Fact(Timeout = 300_000)]
    public async Task AtualizarAsync_DeveGravarComProximaVersionEIncrementarAOrdemEmMemoria()
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();
        await Ordens.AdicionarAsync(ordem);
        var lida = (await Ordens.ObterPorIdAsync(ordem.Id))!;
        lida.IniciarDiagnostico("mecanico-9", Base);

        await Ordens.AtualizarAsync(lida);

        lida.Version.Should().Be(1);
        var documento = await _ambiente.DocumentoDaOrdemAsync(ordem.Id);
        documento!["version"].AsInt64.Should().Be(1);
        documento["status"].AsString.Should().Be("EmDiagnostico");
        (await Ordens.ObterPorIdAsync(ordem.Id))!.Etapas.Should().ContainSingle();
    }

    [Fact(Timeout = 300_000)]
    public async Task AtualizarAsync_ComVersionDesatualizada_DeveLancarConcorrenciaSemAlterarONemOBanco()
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();
        await Ordens.AdicionarAsync(ordem);
        var primeiraLeitura = (await Ordens.ObterPorIdAsync(ordem.Id))!;
        var segundaLeitura = (await Ordens.ObterPorIdAsync(ordem.Id))!;
        primeiraLeitura.IniciarDiagnostico("mecanico-1", Base);
        await Ordens.AtualizarAsync(primeiraLeitura);
        segundaLeitura.Cancelar("Cliente desistiu.", Base);

        var acao = () => Ordens.AtualizarAsync(segundaLeitura);

        await acao.Should().ThrowAsync<ConcorrenciaException>();
        segundaLeitura.Version.Should().Be(0);
        var documento = await _ambiente.DocumentoDaOrdemAsync(ordem.Id);
        documento!["status"].AsString.Should().Be("EmDiagnostico");
        documento["version"].AsInt64.Should().Be(1);
    }

    [Fact(Timeout = 300_000)]
    public async Task AtualizarAsync_DeOrdemInexistente_DeveLancarConcorrencia()
    {
        var acao = () => Ordens.AtualizarAsync(new OrdemDeProducaoBuilder().NaFila());

        await acao.Should().ThrowAsync<ConcorrenciaException>();
    }

    [Fact(Timeout = 300_000)]
    public async Task ObterProximaDaFilaAsync_NaFilaDeDiagnostico_DeveSeguirEnfileiradaEm()
    {
        var maisNova = new OrdemDeProducaoBuilder().EnfileiradaEm(Base.AddMinutes(10)).NaFila();
        var maisAntiga = new OrdemDeProducaoBuilder().EnfileiradaEm(Base).NaFila();
        await Ordens.AdicionarAsync(maisNova);
        await Ordens.AdicionarAsync(maisAntiga);

        (await Ordens.ObterProximaDaFilaAsync(StatusProducao.Fila))!.Id.Should().Be(maisAntiga.Id);
    }

    [Fact(Timeout = 300_000)]
    public async Task ObterProximaDaFilaAsync_NaFilaDeReparo_DeveSeguirLiberadaEmEIgnorarEnfileiradaEm()
    {
        var enfileiradaPrimeiraLiberadaDepois = new OrdemDeProducaoBuilder().EnfileiradaEm(Base).DiagnosticoConcluido();
        enfileiradaPrimeiraLiberadaDepois.LiberarParaExecucao(
            enfileiradaPrimeiraLiberadaDepois.Servicos, enfileiradaPrimeiraLiberadaDepois.Pecas, Base.AddHours(5));
        var enfileiradaDepoisLiberadaPrimeiro = new OrdemDeProducaoBuilder().EnfileiradaEm(Base.AddHours(2)).DiagnosticoConcluido();
        enfileiradaDepoisLiberadaPrimeiro.LiberarParaExecucao(
            enfileiradaDepoisLiberadaPrimeiro.Servicos, enfileiradaDepoisLiberadaPrimeiro.Pecas, Base.AddHours(3));
        await Ordens.AdicionarAsync(enfileiradaPrimeiraLiberadaDepois);
        await Ordens.AdicionarAsync(enfileiradaDepoisLiberadaPrimeiro);

        (await Ordens.ObterProximaDaFilaAsync(StatusProducao.AguardandoExecucao))!.Id
            .Should().Be(enfileiradaDepoisLiberadaPrimeiro.Id);
    }

    [Fact(Timeout = 300_000)]
    public async Task ObterProximaDaFilaAsync_ComFilaVazia_DeveDevolverNull()
    {
        (await Ordens.ObterProximaDaFilaAsync(StatusProducao.Fila)).Should().BeNull();
    }

    [Fact(Timeout = 300_000)]
    public async Task ListarAsync_DeveFiltrarOrdenarPaginarEContar()
    {
        var fila = Enumerable.Range(0, 5)
            .Select(i => new OrdemDeProducaoBuilder().EnfileiradaEm(Base.AddMinutes(-i)).NaFila()).ToList();
        var emDiagnostico = new OrdemDeProducaoBuilder().DoMecanico("mecanico-lista").EmDiagnostico();
        foreach (var ordem in fila.Append(emDiagnostico))
            await Ordens.AdicionarAsync(ordem);

        var primeiraPagina = await Ordens.ListarAsync(StatusProducao.Fila, null, 1, 2);
        var segundaPagina = await Ordens.ListarAsync(StatusProducao.Fila, null, 2, 2);
        var doMecanico = await Ordens.ListarAsync(null, "mecanico-lista", 1, 10);
        var todas = await Ordens.ListarAsync(null, null, 1, 100);

        var esperadas = fila.OrderBy(o => o.EnfileiradaEm).Select(o => o.Id).ToList();
        primeiraPagina.Select(o => o.Id).Should().Equal(esperadas.Take(2));
        segundaPagina.Select(o => o.Id).Should().Equal(esperadas.Skip(2).Take(2));
        doMecanico.Should().ContainSingle().Which.Id.Should().Be(emDiagnostico.Id);
        todas.Should().HaveCount(6);
        (await Ordens.ContarAsync(StatusProducao.Fila, null)).Should().Be(5);
        (await Ordens.ContarAsync(null, "mecanico-lista")).Should().Be(1);
        (await Ordens.ContarAsync(StatusProducao.Cancelada, null)).Should().Be(0);
    }

    [Fact(Timeout = 300_000)]
    public async Task ListarAsync_NaFilaDeReparo_DeveRefletirAOrdemDeConsumo()
    {
        var a = new OrdemDeProducaoBuilder().EnfileiradaEm(Base).DiagnosticoConcluido();
        a.LiberarParaExecucao(a.Servicos, a.Pecas, Base.AddHours(2));
        var b = new OrdemDeProducaoBuilder().EnfileiradaEm(Base.AddHours(1)).DiagnosticoConcluido();
        b.LiberarParaExecucao(b.Servicos, b.Pecas, Base.AddHours(1));
        await Ordens.AdicionarAsync(a);
        await Ordens.AdicionarAsync(b);

        (await Ordens.ListarAsync(StatusProducao.AguardandoExecucao, null, 1, 10)).Select(o => o.Id).Should().Equal(b.Id, a.Id);
    }

    [Fact(Timeout = 300_000)]
    public async Task ContarPorStatusAsync_DeveAgruparPorStatus()
    {
        await Ordens.AdicionarAsync(new OrdemDeProducaoBuilder().NaFila());
        await Ordens.AdicionarAsync(new OrdemDeProducaoBuilder().NaFila());
        await Ordens.AdicionarAsync(new OrdemDeProducaoBuilder().Finalizada());

        (await Ordens.ContarPorStatusAsync()).Should().BeEquivalentTo(new Dictionary<StatusProducao, int>
        {
            [StatusProducao.Fila] = 2,
            [StatusProducao.Finalizada] = 1
        });
    }
}
