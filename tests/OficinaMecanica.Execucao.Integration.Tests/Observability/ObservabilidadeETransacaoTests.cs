using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Execucao.API.Extensions;
using OficinaMecanica.Execucao.Infrastructure.Persistence;
using OficinaMecanica.Execucao.Integration.Tests.Support;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;

namespace OficinaMecanica.Execucao.Integration.Tests.Observability;

public class ObservabilidadeExtensionsTests
{
    private static IConfiguration Configuracao(string? endpoint) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Otel:Endpoint"] = endpoint }).Build();

    [Fact]
    public void AddObservabilidade_SemEndpoint_NaoDeveRegistrarOpenTelemetry()
    {
        var services = new ServiceCollection();

        services.AddObservabilidade(Configuracao(""));

        services.Should().BeEmpty();
    }

    [Fact]
    public void AddObservabilidade_ComEndpoint_DeveRegistrarProvedoresDeTracesEMetricas()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddObservabilidade(Configuracao("http://localhost:4317"));
        using var provider = services.BuildServiceProvider();

        provider.GetService<TracerProvider>().Should().NotBeNull();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void ConfigurarSerilog_ComEndpoint_DeveCriarLoggerComOSinkOtlpSemFalhar()
    {
        using var logger = new LoggerConfiguration().ConfigurarSerilog(Configuracao("http://localhost:4317")).CreateLogger();

        var acao = () => logger.Information("mensagem de teste");

        acao.Should().NotThrow();
    }
}

public class SemTransacaoMongoTests
{
    [Fact]
    public async Task SemTransacao_NaoDeveTerSessaoNemSerAmbienteEToleraQualquerCiclo()
    {
        var transacao = new SemTransacaoMongo();

        await transacao.IniciarAsync(CancellationToken.None);
        await transacao.ConfirmarAsync(CancellationToken.None);
        await transacao.AbortarAsync();

        transacao.Current.Should().BeNull();
        transacao.Ambiente.Should().BeFalse();
    }
}
