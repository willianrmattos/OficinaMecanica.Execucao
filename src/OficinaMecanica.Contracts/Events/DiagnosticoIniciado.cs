namespace OficinaMecanica.Contracts.Events;

public sealed record DiagnosticoIniciado(
    Guid MessageId,
    Guid OrdemDeServicoId,
    DateTimeOffset OcorridoEm,
    string MecanicoId);
