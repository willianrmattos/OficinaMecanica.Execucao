using MediatR;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Application.Queries.ListarFila;

public class ListarFilaQueryHandler : IRequestHandler<ListarFilaQuery, PaginacaoResultDto<OrdemDeProducaoResumoDto>>
{
    private readonly IOrdemDeProducaoRepository _repositorio;

    public ListarFilaQueryHandler(IOrdemDeProducaoRepository repositorio)
    {
        _repositorio = repositorio;
    }

    public async Task<PaginacaoResultDto<OrdemDeProducaoResumoDto>> Handle(ListarFilaQuery request, CancellationToken cancellationToken)
    {
        var ordens = await _repositorio.ListarAsync(
            request.Status, request.MecanicoId, request.Pagina, request.TamanhoPagina, cancellationToken);
        var total = await _repositorio.ContarAsync(request.Status, request.MecanicoId, cancellationToken);

        return new PaginacaoResultDto<OrdemDeProducaoResumoDto>(
            ordens.Select(OrdemDeProducaoMapper.ToResumoDto).ToList(),
            total,
            request.Pagina,
            request.TamanhoPagina,
            (int)Math.Ceiling(total / (double)request.TamanhoPagina));
    }
}
