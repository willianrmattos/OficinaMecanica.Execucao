namespace OficinaMecanica.Contracts.Commands;

public sealed record CancelarProducao(
    Guid MessageId,
    Guid OrdemDeServicoId,
    DateTimeOffset OcorridoEm,
    string Motivo);
