namespace OficinaMecanica.Execucao.Application.DTOs;

public record EtapaProducaoDto(string Nome, string MecanicoId, string Status, DateTimeOffset Inicio, DateTimeOffset? Fim);
