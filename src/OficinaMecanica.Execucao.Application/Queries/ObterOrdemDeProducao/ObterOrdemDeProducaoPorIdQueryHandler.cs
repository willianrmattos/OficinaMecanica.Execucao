using MediatR;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Application.Queries.ObterOrdemDeProducao;

public class ObterOrdemDeProducaoPorIdQueryHandler : IRequestHandler<ObterOrdemDeProducaoPorIdQuery, OrdemDeProducaoDto?>
{
    private readonly IOrdemDeProducaoRepository _repositorio;

    public ObterOrdemDeProducaoPorIdQueryHandler(IOrdemDeProducaoRepository repositorio)
    {
        _repositorio = repositorio;
    }

    public async Task<OrdemDeProducaoDto?> Handle(ObterOrdemDeProducaoPorIdQuery request, CancellationToken cancellationToken)
    {
        var ordem = await _repositorio.ObterPorIdAsync(request.Id, cancellationToken);

        return ordem is null ? null : OrdemDeProducaoMapper.ToDto(ordem);
    }
}
