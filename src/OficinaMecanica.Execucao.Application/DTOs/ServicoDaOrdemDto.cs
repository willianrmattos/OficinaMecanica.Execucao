namespace OficinaMecanica.Execucao.Application.DTOs;

public record ServicoDaOrdemDto(Guid ServicoId, string Nome, int Quantidade, decimal PrecoUnitario);
