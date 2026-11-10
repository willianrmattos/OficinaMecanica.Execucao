using System.Diagnostics.Metrics;
using OficinaMecanica.Execucao.Domain.Enums;

namespace OficinaMecanica.Execucao.Infrastructure.Observability;

// Optei por um buffer por escopo, descarregado so depois do commit: contar no momento da escrita
// inflaria os contadores a cada reexecucao de uma transacao abortada por conflito.
public interface IMetricasPendentes
{
    void OrdemEnfileirada();

    void CancelamentoPrevioRegistrado();

    // Registra o que a gravacao representa conforme o status em que a ordem ficou.
    void OrdemGravada(StatusProducao status);

    void Publicar();

    void Descartar();
}

public sealed class MetricasPendentes : IMetricasPendentes
{
    private readonly List<Counter<long>> _pendentes = new();

    public void OrdemEnfileirada() => _pendentes.Add(MetricasExecucao.OrdensEnfileiradas);

    public void CancelamentoPrevioRegistrado() => _pendentes.Add(MetricasExecucao.CancelamentosPrevios);

    public void OrdemGravada(StatusProducao status)
    {
        var contador = status switch
        {
            StatusProducao.DiagnosticoConcluido => MetricasExecucao.DiagnosticosConcluidos,
            StatusProducao.Finalizada => MetricasExecucao.ReparosFinalizados,
            StatusProducao.Inviavel => MetricasExecucao.OrdensInviabilizadas,
            _ => null
        };

        if (contador is not null)
            _pendentes.Add(contador);
    }

    public void Publicar()
    {
        foreach (var contador in _pendentes)
            contador.Add(1);

        _pendentes.Clear();
    }

    public void Descartar() => _pendentes.Clear();
}
