using System.Diagnostics.Metrics;

namespace OficinaMecanica.Execucao.Infrastructure.Observability;

// O nome do Meter precisa ser registrado com AddMeter no Program.cs: sem isso os instrumentos sao criados
// normalmente mas nao saem via OTLP.
public static class MetricasExecucao
{
    public const string NomeDoMeter = "OficinaMecanica.Execucao";

    private static readonly Meter Meter = new(NomeDoMeter);

    public static readonly Counter<long> OrdensEnfileiradas =
        Meter.CreateCounter<long>("ordens_enfileiradas_total", description: "Ordens de producao enfileiradas.");

    public static readonly Counter<long> DiagnosticosConcluidos =
        Meter.CreateCounter<long>("diagnosticos_concluidos_total", description: "Diagnosticos concluidos pelos mecanicos.");

    public static readonly Counter<long> ReparosFinalizados =
        Meter.CreateCounter<long>("reparos_finalizados_total", description: "Reparos finalizados.");

    public static readonly Counter<long> OrdensInviabilizadas =
        Meter.CreateCounter<long>("ordens_inviabilizadas_total", description: "Ordens marcadas como inviaveis.");

    public static readonly Counter<long> CancelamentosPrevios =
        Meter.CreateCounter<long>("cancelamentos_previos_total", description: "Cancelamentos que chegaram antes da ordem existir.");
}
