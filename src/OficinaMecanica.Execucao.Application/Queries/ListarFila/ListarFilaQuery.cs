using MediatR;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Domain.Enums;

namespace OficinaMecanica.Execucao.Application.Queries.ListarFila;

public record ListarFilaQuery(
    StatusProducao? Status = null,
    string? MecanicoId = null,
    int Pagina = 1,
    int TamanhoPagina = 10
) : IRequest<PaginacaoResultDto<OrdemDeProducaoResumoDto>>;
