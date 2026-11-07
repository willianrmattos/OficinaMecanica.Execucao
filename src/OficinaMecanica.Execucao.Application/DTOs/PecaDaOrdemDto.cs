namespace OficinaMecanica.Execucao.Application.DTOs;

public record PecaDaOrdemDto(Guid PecaId, string Nome, int Quantidade, decimal PrecoUnitario);
