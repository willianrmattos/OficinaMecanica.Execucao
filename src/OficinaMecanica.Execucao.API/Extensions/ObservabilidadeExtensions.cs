using OficinaMecanica.Execucao.Infrastructure.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Sinks.OpenTelemetry;

namespace OficinaMecanica.Execucao.API.Extensions;

public static class ObservabilidadeExtensions
{
    public const string NomeDoServico = "OficinaMecanica.Execucao.API";

    public static LoggerConfiguration ConfigurarSerilog(this LoggerConfiguration logger, IConfiguration configuracao)
    {
        var endpoint = configuracao["Otel:Endpoint"];

        logger = logger
            .ReadFrom.Configuration(configuracao)
            .Enrich.FromLogContext()
            .WriteTo.Console();

        // trace_id/span_id sao populados pelo sink a partir do Activity.Current, sem enricher adicional.
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            logger = logger.WriteTo.OpenTelemetry(opcoes =>
            {
                opcoes.Endpoint = endpoint;
                opcoes.Protocol = OtlpProtocol.Grpc;
                opcoes.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = NomeDoServico };
            });
        }

        return logger;
    }

    // Traces e metricas por OTLP so quando Otel:Endpoint esta configurado (no AKS: OpenTelemetry Collector).
    // A temporalidade das metricas e escolhida por OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE (delta).
    public static IServiceCollection AddObservabilidade(this IServiceCollection services, IConfiguration configuracao)
    {
        var endpoint = configuracao["Otel:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
            return services;

        services.AddOpenTelemetry()
            .ConfigureResource(recurso => recurso.AddService(NomeDoServico))
            .WithTracing(traces => traces
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("MassTransit")
                .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(endpoint)))
            .WithMetrics(metricas => metricas
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(MetricasExecucao.NomeDoMeter)
                .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(endpoint)));

        return services;
    }
}
