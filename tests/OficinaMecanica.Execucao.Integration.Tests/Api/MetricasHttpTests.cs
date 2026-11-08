using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Api;

// O prometheus-net conta a requisição com o status final da resposta. Estes testes sobem o pipeline real (Program.cs) e
// leem o /metrics: com o UseHttpMetrics por dentro do middleware de exceções, todo erro mapeado e todo abandono do cliente
// sairiam como code="200". O registro do prometheus-net é estático no processo, então cada teste compara o /metrics de
// antes com o de depois da própria chamada em vez de ler o valor absoluto.
public class MetricasHttpTests : ApiTestBase
{
    private static readonly Guid OrdemQueNuncaResponde = Guid.NewGuid();

    private readonly TaskCompletionSource _consultaIniciada;

    public MetricasHttpTests() : this(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
    {
    }

    private MetricasHttpTests(TaskCompletionSource consultaIniciada) : base(services => Preparar(services, consultaIniciada))
    {
        _consultaIniciada = consultaIniciada;
    }

    // As ordens consultadas respondem como o fake (não encontrada) e a OrdemQueNuncaResponde fica esperando o cancelamento da
    // requisição, que é o que um timeout do cliente interrompe em produção.
    private static void Preparar(IServiceCollection services, TaskCompletionSource consultaIniciada)
    {
        var repositorio = new Mock<IOrdemDeProducaoRepository>();
        repositorio
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid id, CancellationToken cancelamento) =>
            {
                if (id != OrdemQueNuncaResponde)
                    return null;

                consultaIniciada.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancelamento);
                return (OrdemDeProducao?)null;
            });
        services.RemoveAll<IOrdemDeProducaoRepository>();
        services.AddSingleton(repositorio.Object);
    }

    private async Task<string> LerMetricasAsync() =>
        await (await Api.CreateClient().GetAsync("/metrics")).Content.ReadAsStringAsync();

    private static double Contagem(string metricas, string code, string action) =>
        metricas.Split('\n')
            .Where(l => l.StartsWith("http_requests_received_total{", StringComparison.Ordinal)
                        && l.Contains($"code=\"{code}\"")
                        && l.Contains($"action=\"{action}\""))
            .Sum(l => double.Parse(l[(l.LastIndexOf(' ') + 1)..].Trim(), CultureInfo.InvariantCulture));

    [Fact]
    public async Task OrdemInexistenteMapeadaPeloMiddleware_404_DeveSerContadaComoCode404ENaoComoCode200()
    {
        // O handler lança KeyNotFoundException e é o middleware que a transforma em 404 (o GET da ordem, ao contrário,
        // devolve o 404 direto do controller e não passaria por ele).
        var cliente = Api.CriarClienteAutenticado();
        var antes = await LerMetricasAsync();

        var resposta = await cliente.PostAsJsonAsync($"/api/ordens-de-producao/{Guid.NewGuid()}/diagnostico/concluir", new { laudo = "ok" });

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var depois = await LerMetricasAsync();
        (Contagem(depois, "404", "ConcluirDiagnostico") - Contagem(antes, "404", "ConcluirDiagnostico")).Should().Be(1);
        (Contagem(depois, "200", "ConcluirDiagnostico") - Contagem(antes, "200", "ConcluirDiagnostico")).Should().Be(0);
    }

    [Fact]
    public async Task ErroDeValidacaoMapeadoPeloMiddleware_400_DeveSerContadoComoCode400ENaoComoCode200()
    {
        var cliente = Api.CriarClienteAutenticado();
        var antes = await LerMetricasAsync();

        var resposta = await cliente.PostAsJsonAsync($"/api/ordens-de-producao/{Guid.NewGuid()}/diagnostico/concluir", new { });

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var depois = await LerMetricasAsync();
        (Contagem(depois, "400", "ConcluirDiagnostico") - Contagem(antes, "400", "ConcluirDiagnostico")).Should().Be(1);
        (Contagem(depois, "200", "ConcluirDiagnostico") - Contagem(antes, "200", "ConcluirDiagnostico")).Should().Be(0);
    }

    [Fact]
    public async Task ClienteQueAbandonaARequisicao_DeveSerContadoComoCode499ENaoComoCode200()
    {
        var cliente = Api.CriarClienteAutenticado();
        var antes = await LerMetricasAsync();
        using var abandono = new CancellationTokenSource();

        var chamada = cliente.GetAsync($"/api/ordens-de-producao/{OrdemQueNuncaResponde}", abandono.Token);
        await _consultaIniciada.Task.ComTetoAsync("o handler nao chegou a consultar a ordem");
        await abandono.CancelAsync();
        var acao = () => chamada;
        await acao.Should().ThrowAsync<OperationCanceledException>();

        var contou499 = await Esperar.AteAsync(
            async () => Contagem(await LerMetricasAsync(), "499", "ObterPorId") - Contagem(antes, "499", "ObterPorId") == 1,
            limiteEmMs: 5_000);
        contou499.Should().BeTrue("a requisição abandonada tem de aparecer no /metrics como code=\"499\"");
        var depois = await LerMetricasAsync();
        (Contagem(depois, "200", "ObterPorId") - Contagem(antes, "200", "ObterPorId")).Should().Be(0);
    }
}
