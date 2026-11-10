using System.Net;
using System.Net.Http.Json;
using OficinaMecanica.Contracts.Commands;
using OficinaMecanica.Contracts.Events;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Infra;

[Trait("Category", "Infra")]
[Collection(Colecoes.Infra)]
public class FluxoDeProducaoTests(InfraFixture infra)
{
    [Fact(Timeout = 300_000)]
    public async Task FluxoCompleto_DaSolicitacaoAteAFinalizacao_DevePersistirEPublicarCadaEventoUmaVez()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
        var ordemId = Guid.NewGuid();

        // OS Service pede o diagnostico; o consumer enfileira.
        await ambiente.Os.EnviarAsync(new SolicitarDiagnosticoBuilder().ComOrdemDeServicoId(ordemId).Build());
        (await Esperar.AteAsync(async () => await ambiente.ConsultarAsync(ordemId) is not null)).Should().BeTrue();

        // Mecanico reivindica pela API; DiagnosticoIniciado sai pelo outbox.
        var mecanico = ambiente.Cliente("mecanico-42");
        var claim = await mecanico.PostAsync("/api/diagnosticos/proximo", null);
        claim.StatusCode.Should().Be(HttpStatusCode.OK);
        var emDiagnostico = await claim.Content.ReadFromJsonAsync<OrdemDeProducaoDto>();
        emDiagnostico!.Id.Should().Be(ordemId);
        emDiagnostico.MecanicoId.Should().Be("mecanico-42");
        (await Esperar.AteAsync(() => ambiente.Os.Gravador.De<DiagnosticoIniciado>().Count == 1)).Should().BeTrue();

        // Conclui o diagnostico; DiagnosticoConcluido (com o laudo) sai pelo outbox.
        var concluir = await mecanico.PostAsJsonAsync($"/api/ordens-de-producao/{ordemId}/diagnostico/concluir",
            new { laudo = "Pastilhas gastas.", horasEstimadas = 2.5 });
        concluir.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Esperar.AteAsync(() => ambiente.Os.Gravador.De<DiagnosticoConcluido>().Count == 1)).Should().BeTrue();

        // OS Service libera a execucao com o snapshot aprovado; a ordem vai para a fila de reparo.
        await ambiente.Os.EnviarAsync(new IniciarExecucaoBuilder().ComOrdemDeServicoId(ordemId).Build());
        (await Esperar.AteAsync(async () => (await ambiente.ConsultarAsync(ordemId))!.Status == "AguardandoExecucao")).Should().BeTrue();

        // Reparo e finalizacao.
        var reparo = await mecanico.PostAsync("/api/reparos/proximo", null);
        reparo.StatusCode.Should().Be(HttpStatusCode.OK);
        var finalizar = await mecanico.PostAsJsonAsync($"/api/ordens-de-producao/{ordemId}/reparo/finalizar",
            new { resumo = "Pastilhas trocadas." });
        finalizar.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Esperar.AteAsync(() => ambiente.Os.Gravador.De<ExecucaoFinalizada>().Count == 1)).Should().BeTrue();

        (await ambiente.ConsultarAsync(ordemId))!.Status.Should().Be("Finalizada");
        ambiente.Os.Gravador.Falhas.Should().Be(0);
        ambiente.Os.Gravador.De<DiagnosticoIniciado>().Should().ContainSingle().Which.OrdemDeServicoId.Should().Be(ordemId);
    }
}
