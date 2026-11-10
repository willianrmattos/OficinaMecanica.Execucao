namespace OficinaMecanica.Contracts.Events;

public sealed record DiagnosticoConcluido(
    Guid MessageId,
    Guid OrdemDeServicoId,
    DateTimeOffset OcorridoEm,
    string MecanicoId,
    string Laudo);
