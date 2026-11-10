namespace OficinaMecanica.Execucao.Application.DTOs;

public record ResumoDaFilaDto(IReadOnlyDictionary<string, int> PorStatus, int Total);
