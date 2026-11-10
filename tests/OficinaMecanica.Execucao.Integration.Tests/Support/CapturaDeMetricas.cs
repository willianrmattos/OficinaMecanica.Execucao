using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using OficinaMecanica.Execucao.Infrastructure.Observability;

namespace OficinaMecanica.Execucao.Integration.Tests.Support;

// Escuta o Meter do servico e acumula o que cada instrumento reportou, com as tags.
public sealed class CapturaDeMetricas : IDisposable
{
    public sealed record Medicao(string Instrumento, double Valor, IReadOnlyDictionary<string, object?> Tags);

    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<Medicao> _medicoes = new();

    public CapturaDeMetricas()
    {
        _listener.InstrumentPublished = (instrumento, listener) =>
        {
            if (instrumento.Meter.Name == MetricasExecucao.NomeDoMeter)
                listener.EnableMeasurementEvents(instrumento);
        };
        _listener.SetMeasurementEventCallback<long>((instrumento, valor, tags, _) => Registrar(instrumento, valor, tags));
        _listener.SetMeasurementEventCallback<double>((instrumento, valor, tags, _) => Registrar(instrumento, valor, tags));
        _listener.Start();
    }

    public IReadOnlyList<Medicao> Medicoes => _medicoes.ToArray();

    public double Total(string instrumento) => Medicoes.Where(m => m.Instrumento == instrumento).Sum(m => m.Valor);

    // Dispara a leitura dos instrumentos observaveis (gauges) e devolve so o que eles reportaram agora.
    public IReadOnlyList<Medicao> LerObservaveis()
    {
        var antes = _medicoes.Count;
        _listener.RecordObservableInstruments();
        return _medicoes.Skip(antes).ToList();
    }

    private void Registrar(Instrument instrumento, double valor, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        _medicoes.Enqueue(new Medicao(instrumento.Name, valor, tags.ToArray().ToDictionary(t => t.Key, t => t.Value)));

    public void Dispose() => _listener.Dispose();
}
