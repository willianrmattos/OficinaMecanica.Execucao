namespace OficinaMecanica.Execucao.Application.DTOs;

public record OrdemDeProducaoDto(
    Guid Id,
    string Numero,
    VeiculoDaOrdemDto Veiculo,
    string? Observacoes,
    string Status,
    string? MecanicoId,
    IEnumerable<ServicoDaOrdemDto> Servicos,
    IEnumerable<PecaDaOrdemDto> Pecas,
    IEnumerable<EtapaProducaoDto> Etapas,
    IEnumerable<ApontamentoDto> Apontamentos,
    LaudoDto? Laudo,
    string? Resumo,
    string? MotivoInviabilidade,
    string? InviabilizadaPor,
    string? MotivoCancelamento,
    DateTimeOffset EnfileiradaEm,
    DateTimeOffset? LiberadaEm
);
