namespace OficinaMecanica.Execucao.Domain.Enums;

public enum StatusProducao
{
    Fila = 1,
    EmDiagnostico = 2,
    DiagnosticoConcluido = 3,
    AguardandoExecucao = 4,
    EmExecucao = 5,
    Finalizada = 6,
    Inviavel = 7,
    Cancelada = 8
}
