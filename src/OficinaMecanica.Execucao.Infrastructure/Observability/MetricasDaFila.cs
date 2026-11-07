using System.Diagnostics.Metrics;
using OficinaMecanica.Execucao.Domain.Enums;

namespace OficinaMecanica.Execucao.Infrastructure.Observability;

// Os instrumentos observaveis sao lidos de forma sincrona pelo coletor, entao nao podem consultar o banco:
// leem o ultimo retrato gravado por MetricasDaFilaBackgroundService. A idade e calculada na hora da leitura
// a partir do inicio da ordem mais antiga, para continuar crescendo entre dois retratos.
public sealed class MetricasDaFila : IDisposable
{
    // Os status que carregam SLA de diagnostico e de reparo. Reporto os quatro sempre, com 0 quando nao ha ordem:
    // uma serie que deixa de ser reportada fica congelada no ultimo valor em quem faz scrape (Prometheus).
    private static readonly StatusProducao[] StatusComSla =
    [
        StatusProducao.Fila,
        StatusProducao.EmDiagnostico,
        StatusProducao.AguardandoExecucao,
        StatusProducao.EmExecucao
    ];

    private sealed record Retrato(
        IReadOnlyDictionary<string, int> Tamanhos,
        IReadOnlyDictionary<string, DateTimeOffset> InicioDaMaisAntiga,
        long OutboxPendente);

    private readonly Meter _meter = new(MetricasExecucao.NomeDoMeter);
    private readonly TimeProvider _relogio;
    private volatile Retrato _retrato = new(new Dictionary<string, int>(), new Dictionary<string, DateTimeOffset>(), 0);

    public MetricasDaFila(TimeProvider relogio)
    {
        _relogio = relogio;

        _meter.CreateObservableGauge(
            "fila_ordens",
            ObservarTamanhos,
            unit: "{ordem}",
            description: "Ordens de producao por status.");

        _meter.CreateObservableGauge(
            "fila_idade_mais_antiga_segundos",
            ObservarIdades,
            unit: "s",
            description: "Idade da ordem mais antiga por status (alerta de SLA; so metrica, nada e cancelado).");

        _meter.CreateObservableGauge(
            "outbox_mensagens_pendentes",
            () => _retrato.OutboxPendente,
            unit: "{mensagem}",
            description: "Mensagens no outbox ainda nao entregues ao broker (alerta de broker fora).");
    }

    public void Atualizar(
        IReadOnlyDictionary<StatusProducao, int> tamanhos,
        IReadOnlyDictionary<StatusProducao, DateTimeOffset> inicioDaMaisAntiga,
        long outboxPendente = 0) =>
        _retrato = new Retrato(
            tamanhos.ToDictionary(x => x.Key.ToString(), x => x.Value),
            inicioDaMaisAntiga.ToDictionary(x => x.Key.ToString(), x => x.Value),
            outboxPendente);

    private IEnumerable<Measurement<long>> ObservarTamanhos() =>
        _retrato.Tamanhos.Select(x => new Measurement<long>(x.Value, new KeyValuePair<string, object?>("status", x.Key)));

    private IEnumerable<Measurement<double>> ObservarIdades()
    {
        var agora = _relogio.GetUtcNow();
        var inicios = _retrato.InicioDaMaisAntiga;

        return StatusComSla.Select(status => new Measurement<double>(
            inicios.TryGetValue(status.ToString(), out var inicio) ? Math.Max(0, (agora - inicio).TotalSeconds) : 0,
            new KeyValuePair<string, object?>("status", status.ToString())));
    }

    public void Dispose() => _meter.Dispose();
}
