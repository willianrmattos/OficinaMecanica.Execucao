using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMecanica.Execucao.Application;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Infrastructure.Observability;
using OficinaMecanica.Execucao.Integration.Tests.Fakes;
using OficinaMecanica.Execucao.Integration.Tests.Support;
using OficinaMecanica.Execucao.Tests.Common;

namespace OficinaMecanica.Execucao.Integration.Tests.Observability;

public class MetricasPendentesTests
{
    [Fact]
    public void Publicar_DeveIncrementarCadaContadorUmaVezEEsvaziarOBuffer()
    {
        using var captura = new CapturaDeMetricas();
        var metricas = new MetricasPendentes();

        metricas.OrdemEnfileirada();
        metricas.CancelamentoPrevioRegistrado();
        metricas.OrdemGravada(StatusProducao.DiagnosticoConcluido);
        metricas.OrdemGravada(StatusProducao.Finalizada);
        metricas.OrdemGravada(StatusProducao.Inviavel);
        metricas.Publicar();
        metricas.Publicar();

        captura.Total("ordens_enfileiradas_total").Should().Be(1);
        captura.Total("cancelamentos_previos_total").Should().Be(1);
        captura.Total("diagnosticos_concluidos_total").Should().Be(1);
        captura.Total("reparos_finalizados_total").Should().Be(1);
        captura.Total("ordens_inviabilizadas_total").Should().Be(1);
    }

    [Fact]
    public void AntesDePublicar_NadaDeveSerContado()
    {
        using var captura = new CapturaDeMetricas();
        var metricas = new MetricasPendentes();

        metricas.OrdemEnfileirada();
        metricas.OrdemGravada(StatusProducao.Finalizada);

        captura.Medicoes.Should().BeEmpty();
    }

    [Fact]
    public void Descartar_DeveApagarOQueEstavaPendente()
    {
        using var captura = new CapturaDeMetricas();
        var metricas = new MetricasPendentes();
        metricas.OrdemEnfileirada();

        metricas.Descartar();
        metricas.Publicar();

        captura.Medicoes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(StatusProducao.Fila)]
    [InlineData(StatusProducao.EmDiagnostico)]
    [InlineData(StatusProducao.AguardandoExecucao)]
    [InlineData(StatusProducao.EmExecucao)]
    [InlineData(StatusProducao.Cancelada)]
    public void OrdemGravadaEmStatusSemMetrica_NaoDeveContarNada(StatusProducao status)
    {
        using var captura = new CapturaDeMetricas();
        var metricas = new MetricasPendentes();

        metricas.OrdemGravada(status);
        metricas.Publicar();

        captura.Medicoes.Should().BeEmpty();
    }
}

public class MetricasDaFilaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SemRetrato_NaoDeveReportarTamanhoEDeveReportarZeroParaIdadeEOutbox()
    {
        using var captura = new CapturaDeMetricas();
        using var metricas = new MetricasDaFila(new RelogioFixo(Agora));

        var leitura = captura.LerObservaveis();

        leitura.Should().OnlyContain(m => m.Instrumento != "fila_ordens" && m.Valor == 0);
        leitura.Where(m => m.Instrumento == "fila_idade_mais_antiga_segundos").Select(m => (string)m.Tags["status"]!)
            .Should().BeEquivalentTo("Fila", "EmDiagnostico", "AguardandoExecucao", "EmExecucao");
        leitura.Should().ContainSingle(m => m.Instrumento == "outbox_mensagens_pendentes");
    }

    [Fact]
    public void Atualizar_ComMensagensNoOutbox_DeveReportarOGaugeDeBrokerFora()
    {
        using var captura = new CapturaDeMetricas();
        using var metricas = new MetricasDaFila(new RelogioFixo(Agora));

        metricas.Atualizar(new Dictionary<StatusProducao, int>(), new Dictionary<StatusProducao, DateTimeOffset>(), outboxPendente: 17);

        captura.LerObservaveis().Single(m => m.Instrumento == "outbox_mensagens_pendentes").Valor.Should().Be(17);
    }

    [Fact]
    public void Atualizar_DeveReportarTamanhoPorStatusEIdadeDaMaisAntigaCalculadaNaLeitura()
    {
        using var captura = new CapturaDeMetricas();
        using var metricas = new MetricasDaFila(new RelogioFixo(Agora));

        metricas.Atualizar(
            new Dictionary<StatusProducao, int> { [StatusProducao.Fila] = 4, [StatusProducao.EmExecucao] = 1 },
            new Dictionary<StatusProducao, DateTimeOffset>
            {
                [StatusProducao.Fila] = Agora.AddMinutes(-90),
                [StatusProducao.EmExecucao] = Agora.AddSeconds(30)
            });
        var leitura = captura.LerObservaveis();

        leitura.Where(m => m.Instrumento == "fila_ordens")
            .ToDictionary(m => (string)m.Tags["status"]!, m => m.Valor)
            .Should().BeEquivalentTo(new Dictionary<string, double> { ["Fila"] = 4, ["EmExecucao"] = 1 });
        var idades = leitura.Where(m => m.Instrumento == "fila_idade_mais_antiga_segundos")
            .ToDictionary(m => (string)m.Tags["status"]!, m => m.Valor);
        idades["Fila"].Should().Be(90 * 60);
        idades["EmExecucao"].Should().Be(0, "inicio no futuro nao gera idade negativa");
        idades["EmDiagnostico"].Should().Be(0, "sem ordem no status a idade e 0, nao uma serie congelada");
        idades["AguardandoExecucao"].Should().Be(0);
    }

    [Fact]
    public void Atualizar_DeNovo_DeveSubstituirORetratoAnterior()
    {
        using var captura = new CapturaDeMetricas();
        using var metricas = new MetricasDaFila(new RelogioFixo(Agora));
        metricas.Atualizar(new Dictionary<StatusProducao, int> { [StatusProducao.Fila] = 9 }, new Dictionary<StatusProducao, DateTimeOffset>());

        metricas.Atualizar(new Dictionary<StatusProducao, int> { [StatusProducao.Finalizada] = 2 }, new Dictionary<StatusProducao, DateTimeOffset>());
        var leitura = captura.LerObservaveis();

        leitura.Where(m => m.Instrumento == "fila_ordens").Should().ContainSingle().Which.Tags["status"].Should().Be("Finalizada");
    }
}

public class MetricasDaFilaBackgroundServiceTests
{
    private sealed class LeitorDeIdadeQueFalha : IIdadeDaFilaReader
    {
        public Task<IReadOnlyDictionary<StatusProducao, DateTimeOffset>> ObterInicioDaMaisAntigaAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("banco fora");
    }

    private static ServiceProvider CriarProvider(IIdadeDaFilaReader leitor, FakeOrdemDeProducaoRepository ordens)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<IOrdemDeProducaoRepository>(ordens);
        services.AddSingleton<ICancelamentoPrevioRepository, FakeCancelamentoPrevioRepository>();
        services.AddSingleton<IUnitOfWork, FakeUnitOfWork>();
        services.AddSingleton(leitor);
        services.AddSingleton<IOutboxPendenteReader>(new FakeOutboxPendenteReader { Pendentes = 3 });
        return services.BuildServiceProvider();
    }

    private static MetricasDaFilaBackgroundService CriarServico(ServiceProvider provider, MetricasDaFila metricas, int intervalo = 15) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), metricas, new MetricasDaFilaOptions { IntervaloDeAtualizacaoEmSegundos = intervalo },
            NullLogger<MetricasDaFilaBackgroundService>.Instance);

    [Fact]
    public async Task AtualizarAsync_DeveAlimentarOsGaugesComOResumoDaFilaEAIdadeDaMaisAntiga()
    {
        using var captura = new CapturaDeMetricas();
        var agora = DateTimeOffset.UtcNow;
        var ordens = new FakeOrdemDeProducaoRepository();
        ordens.Semear(new OrdemDeProducaoBuilder().NaFila());
        ordens.Semear(new OrdemDeProducaoBuilder().NaFila());
        var leitor = new FakeIdadeDaFilaReaderComDados(new Dictionary<StatusProducao, DateTimeOffset> { [StatusProducao.Fila] = agora.AddMinutes(-10) });
        using var provider = CriarProvider(leitor, ordens);
        using var metricas = new MetricasDaFila(TimeProvider.System);

        await CriarServico(provider, metricas).AtualizarAsync(CancellationToken.None);
        var leitura = captura.LerObservaveis();

        leitura.Single(m => m.Instrumento == "fila_ordens" && (string)m.Tags["status"]! == "Fila").Valor.Should().Be(2);
        leitura.Single(m => m.Instrumento == "fila_ordens" && (string)m.Tags["status"]! == "Finalizada").Valor.Should().Be(0);
        leitura.Single(m => m.Instrumento == "fila_idade_mais_antiga_segundos" && (string)m.Tags["status"]! == "Fila")
            .Valor.Should().BeApproximately(600, 5);
        leitura.Single(m => m.Instrumento == "outbox_mensagens_pendentes").Valor.Should().Be(3);
    }

    [Fact]
    public async Task AtualizarAsync_ComFalhaDeLeitura_NaoDeveLancarEDeveManterOUltimoRetrato()
    {
        using var captura = new CapturaDeMetricas();
        var ordens = new FakeOrdemDeProducaoRepository();
        ordens.Semear(new OrdemDeProducaoBuilder().NaFila());
        using var providerBom = CriarProvider(new FakeIdadeDaFilaReader(), ordens);
        using var metricas = new MetricasDaFila(TimeProvider.System);
        await CriarServico(providerBom, metricas).AtualizarAsync(CancellationToken.None);

        using var providerRuim = CriarProvider(new LeitorDeIdadeQueFalha(), ordens);
        var acao = () => CriarServico(providerRuim, metricas).AtualizarAsync(CancellationToken.None);

        await acao.Should().NotThrowAsync();
        captura.LerObservaveis().Single(m => m.Instrumento == "fila_ordens" && (string)m.Tags["status"]! == "Fila").Valor.Should().Be(1);
    }

    [Fact]
    public async Task ExecucaoEmSegundoPlano_DeveAtualizarNoInicioEEncerrarSemErroAoParar()
    {
        using var captura = new CapturaDeMetricas();
        var ordens = new FakeOrdemDeProducaoRepository();
        ordens.Semear(new OrdemDeProducaoBuilder().NaFila());
        using var provider = CriarProvider(new FakeIdadeDaFilaReader(), ordens);
        using var metricas = new MetricasDaFila(TimeProvider.System);
        using var servico = CriarServico(provider, metricas, intervalo: 1);

        await servico.StartAsync(CancellationToken.None);
        (await Esperar.AteAsync(() => captura.LerObservaveis().Any(m => m.Instrumento == "fila_ordens"), 5_000)).Should().BeTrue();
        var parar = () => servico.StopAsync(CancellationToken.None);

        await parar.Should().NotThrowAsync();
    }

    private sealed class FakeIdadeDaFilaReaderComDados(IReadOnlyDictionary<StatusProducao, DateTimeOffset> dados) : IIdadeDaFilaReader
    {
        public Task<IReadOnlyDictionary<StatusProducao, DateTimeOffset>> ObterInicioDaMaisAntigaAsync(CancellationToken cancellationToken) =>
            Task.FromResult(dados);
    }
}
