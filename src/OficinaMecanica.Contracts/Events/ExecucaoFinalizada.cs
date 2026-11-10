namespace OficinaMecanica.Contracts.Events;

public sealed record ExecucaoFinalizada(
    Guid MessageId,
    Guid OrdemDeServicoId,
    DateTimeOffset OcorridoEm,
    string Resumo,
    DateTimeOffset FinalizadaEm);
