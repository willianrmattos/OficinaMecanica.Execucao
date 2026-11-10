using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using OficinaMecanica.Contracts.Events;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Infra;

// Estado da ordem e mensagem do outbox commitam juntos ou nao commitam: nenhuma falha entre a escrita e o
// commit pode deixar documento sem evento (ou evento sem documento).
[Trait("Category", "Infra")]
[Collection(Colecoes.Infra)]
public class OutboxAtomicoTests(InfraFixture infra)
{
    private const string Mecanico = "mecanico-atomico";

    [Fact(Timeout = 300_000)]
    public async Task ConcluirDiagnostico_ComFalhaDepoisDePublicar_DeveDesfazerODocumentoEZerarOOutbox()
    {
        var falha = new InterruptorDeFalha();
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra, services =>
        {
            services.AddSingleton(falha);
            services.Decorar<IIntegrationEventPublisher>((interno, sp) =>
                new PublicadorQueFalhaAposPublicar(interno, sp.GetRequiredService<InterruptorDeFalha>()));
        });
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmDiagnostico();
        await ambiente.SemearAsync(ordem);

        falha.Ligar();
        var resposta = await ambiente.Cliente(Mecanico).PostAsJsonAsync(
            $"/api/ordens-de-producao/{ordem.Id}/diagnostico/concluir", new { laudo = "Correia partida." });

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var documento = await ambiente.DocumentoDaOrdemAsync(ordem.Id);
        documento!["status"].AsString.Should().Be("EmDiagnostico");
        documento["version"].AsInt64.Should().Be(0);
        documento.Contains("laudo").Should().BeFalse();
        (await ambiente.ContarNoBancoAsync("outbox.messages")).Should().Be(0);
        await Task.Delay(1_500);
        ambiente.Os.Gravador.De<DiagnosticoConcluido>().Should().BeEmpty();
    }

    [Fact(Timeout = 300_000)]
    public async Task ConcluirDiagnostico_AposAFalhaSerRemovida_DeveCommitarDocumentoEEventoJuntos()
    {
        var falha = new InterruptorDeFalha();
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra, services =>
        {
            services.AddSingleton(falha);
            services.Decorar<IIntegrationEventPublisher>((interno, sp) =>
                new PublicadorQueFalhaAposPublicar(interno, sp.GetRequiredService<InterruptorDeFalha>()));
        });
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmDiagnostico();
        await ambiente.SemearAsync(ordem);
        falha.Ligar();
        (await ambiente.Cliente(Mecanico).PostAsJsonAsync(
            $"/api/ordens-de-producao/{ordem.Id}/diagnostico/concluir", new { laudo = "Correia partida." }))
            .StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        falha.Desligar();
        var segunda = await ambiente.Cliente(Mecanico).PostAsJsonAsync(
            $"/api/ordens-de-producao/{ordem.Id}/diagnostico/concluir", new { laudo = "Correia partida." });

        segunda.StatusCode.Should().Be(HttpStatusCode.OK);
        var documento = await ambiente.DocumentoDaOrdemAsync(ordem.Id);
        documento!["status"].AsString.Should().Be("DiagnosticoConcluido");
        documento["version"].AsInt64.Should().Be(1);
        (await Esperar.AteAsync(() => ambiente.Os.Gravador.De<DiagnosticoConcluido>().Count == 1)).Should().BeTrue();
        await Task.Delay(500);
        ambiente.Os.Gravador.De<DiagnosticoConcluido>().Should().ContainSingle();
    }

    [Fact(Timeout = 300_000)]
    public async Task ReivindicarDiagnostico_ComFalhaDepoisDePublicar_DeveManterAOrdemNaFilaSemEvento()
    {
        var falha = new InterruptorDeFalha();
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra, services =>
        {
            services.AddSingleton(falha);
            services.Decorar<IIntegrationEventPublisher>((interno, sp) =>
                new PublicadorQueFalhaAposPublicar(interno, sp.GetRequiredService<InterruptorDeFalha>()));
        });
        var ordem = new OrdemDeProducaoBuilder().NaFila();
        await ambiente.SemearAsync(ordem);

        falha.Ligar();
        var resposta = await ambiente.Cliente(Mecanico).PostAsync("/api/diagnosticos/proximo", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var documento = await ambiente.DocumentoDaOrdemAsync(ordem.Id);
        documento!["status"].AsString.Should().Be("Fila");
        documento.Contains("mecanicoId").Should().BeFalse();
        (await ambiente.ContarNoBancoAsync("outbox.messages")).Should().Be(0);
        await Task.Delay(1_500);
        ambiente.Os.Gravador.De<DiagnosticoIniciado>().Should().BeEmpty();

        falha.Desligar();
        (await ambiente.Cliente(Mecanico).PostAsync("/api/diagnosticos/proximo", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Esperar.AteAsync(() => ambiente.Os.Gravador.De<DiagnosticoIniciado>().Count == 1)).Should().BeTrue();
    }

    [Fact(Timeout = 300_000)]
    public async Task ConsumerComFalhaAposAEscrita_DeveEsgotarRetentativasSemDeixarOrdemEReprocessarNoReenvio()
    {
        var falha = new InterruptorDeFalha();
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra, services =>
        {
            services.AddSingleton(falha);
            services.AddSingleton<ContadorDeChamadas>();
            services.Decorar<IUnitOfWork>((interno, sp) =>
                new UnitOfWorkQueFalha(interno, sp.GetRequiredService<InterruptorDeFalha>(), sp.GetRequiredService<ContadorDeChamadas>()));
        });
        var mensagem = new SolicitarDiagnosticoBuilder().Build();

        falha.Ligar();
        await ambiente.Os.EnviarAsync(mensagem);

        // 1 tentativa + 3 retentativas configuradas, depois a mensagem vai para a fila de erro e o Fault e publicado.
        (await Esperar.AteAsync(() => ambiente.Os.Gravador.Falhas == 1)).Should().BeTrue();
        (await ambiente.DocumentoDaOrdemAsync(mensagem.OrdemDeServicoId)).Should().BeNull();

        // O registro do inbox fica sem Consumed: o reenvio da mesma mensagem reprocessa de verdade.
        falha.Desligar();
        await ambiente.Os.EnviarAsync(mensagem);

        (await Esperar.AteAsync(async () => await ambiente.DocumentoDaOrdemAsync(mensagem.OrdemDeServicoId) is not null))
            .Should().BeTrue();
        var documento = await ambiente.DocumentoDaOrdemAsync(mensagem.OrdemDeServicoId);
        documento!["_t"].AsString.Should().Be("ordem");
        documento["status"].AsString.Should().Be("Fila");
    }
}
