using OficinaMecanica.Contracts.Common;

namespace OficinaMecanica.Contracts.Commands;

public sealed record SolicitarDiagnostico(
    Guid MessageId,
    Guid OrdemDeServicoId,
    DateTimeOffset OcorridoEm,
    string Numero,
    string Placa,
    string Marca,
    string Modelo,
    string? Observacoes,
    IReadOnlyList<ItemDeServicoMensagem> Servicos,
    IReadOnlyList<ItemDePecaMensagem> Pecas);
