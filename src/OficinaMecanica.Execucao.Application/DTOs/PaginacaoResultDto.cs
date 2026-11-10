namespace OficinaMecanica.Execucao.Application.DTOs;

public record PaginacaoResultDto<T>(
    IEnumerable<T> Itens,
    int Total,
    int Pagina,
    int TamanhoPagina,
    int TotalPaginas
);
