namespace OficinaMecanica.Contracts.Events;

public sealed record ExecucaoInviavel(
    Guid MessageId,
    Guid OrdemDeServicoId,
    DateTimeOffset OcorridoEm,
    string Motivo);
