namespace OficinaMecanica.Execucao.Application.DTOs;

public record OrdemDeProducaoResumoDto(
    Guid Id,
    string Numero,
    string Placa,
    string Marca,
    string Modelo,
    string Status,
    string? MecanicoId,
    DateTimeOffset EnfileiradaEm
);
