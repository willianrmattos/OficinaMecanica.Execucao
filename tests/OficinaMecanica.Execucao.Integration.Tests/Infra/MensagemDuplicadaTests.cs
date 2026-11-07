using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Infra;

// Entrega at-least-once: o mesmo MessageId pode chegar varias vezes (e ate em paralelo). O inbox do outbox
// barra a duplicata antes do handler, e a idempotencia natural do dominio e a segunda defesa.
[Trait("Category", "Infra")]
[Collection(Colecoes.Infra)]
public class MensagemDuplicadaTests(InfraFixture infra)
{
    private static Task<AmbienteDeInfra> CriarAmbienteAsync(InfraFixture infra, ContadorDeChamadas contador) =>
        AmbienteDeInfra.CriarAsync(infra, services =>
        {
            services.AddSingleton(contador);
            services.AddSingleton<InterruptorDeFalha>();
            services.Decorar<IUnitOfWork>((interno, sp) =>
                new UnitOfWorkQueFalha(interno, sp.GetRequiredService<InterruptorDeFalha>(), sp.GetRequiredService<ContadorDeChamadas>()));
        });

    [Fact(Timeout = 300_000)]
    public async Task MesmoMessageIdTresVezes_DeveExecutarOHandlerUmaVezECriarUmaOrdem()
    {
        var contador = new ContadorDeChamadas();
        await using var ambiente = await CriarAmbienteAsync(infra, contador);
        var mensagem = new SolicitarDiagnosticoBuilder().Build();

        for (var i = 0; i < 3; i++)
            await ambiente.Os.EnviarAsync(mensagem);

        (await Esperar.AteAsync(async () => await ambiente.DocumentoDaOrdemAsync(mensagem.OrdemDeServicoId) is not null))
            .Should().BeTrue();
        await Task.Delay(2_000);

        contador.Total.Should().Be(1);
        (await ambiente.Documentos.CountDocumentsAsync(new BsonDocument())).Should().Be(1);
        (await ambiente.ContarNoBancoAsync("inbox.states")).Should().Be(1);
        ambiente.Os.Gravador.Falhas.Should().Be(0);
    }

    [Fact(Timeout = 300_000)]
    public async Task CopiasConcorrentesDoMesmoMessageId_DevemProcessarUmaVezSemIrParaAFilaDeErro()
    {
        var contador = new ContadorDeChamadas();
        await using var ambiente = await CriarAmbienteAsync(infra, contador);
        var mensagem = new SolicitarDiagnosticoBuilder().Build();

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => ambiente.Os.EnviarAsync(mensagem)));

        (await Esperar.AteAsync(async () => await ambiente.DocumentoDaOrdemAsync(mensagem.OrdemDeServicoId) is not null))
            .Should().BeTrue();
        await Task.Delay(2_500);

        contador.Total.Should().Be(1);
        (await ambiente.Documentos.CountDocumentsAsync(new BsonDocument())).Should().Be(1);
        ambiente.Os.Gravador.Falhas.Should().Be(0);
    }

    [Fact(Timeout = 300_000)]
    public async Task MesmaOrdemComMessageIdsDiferentes_DeveCriarUmaOrdemESegundaEntregaVirarNoOp()
    {
        var contador = new ContadorDeChamadas();
        await using var ambiente = await CriarAmbienteAsync(infra, contador);
        var ordemId = Guid.NewGuid();
        var primeira = new SolicitarDiagnosticoBuilder().ComOrdemDeServicoId(ordemId).Build();
        var reenvio = primeira with { MessageId = Guid.NewGuid() };

        await ambiente.Os.EnviarAsync(primeira);
        (await Esperar.AteAsync(async () => await ambiente.DocumentoDaOrdemAsync(ordemId) is not null)).Should().BeTrue();
        await ambiente.Os.EnviarAsync(reenvio);
        (await Esperar.AteAsync(async () =>
                await ambiente.Api.Banco.GetCollection<BsonDocument>("inbox.states")
                    .CountDocumentsAsync(new BsonDocument("Consumed", new BsonDocument("$ne", BsonNull.Value))) == 2))
            .Should().BeTrue();

        (await ambiente.Documentos.CountDocumentsAsync(new BsonDocument())).Should().Be(1);
        (await ambiente.ContarNoBancoAsync("inbox.states")).Should().Be(2);
        ambiente.Os.Gravador.Falhas.Should().Be(0);
    }
}
