using MediatR;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Application.Commands.RegistrarApontamento;

public class RegistrarApontamentoCommandHandler : IRequestHandler<RegistrarApontamentoCommand, OrdemDeProducaoDto>
{
    private readonly IOrdemDeProducaoRepository _repositorio;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public RegistrarApontamentoCommandHandler(
        IOrdemDeProducaoRepository repositorio,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _repositorio = repositorio;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<OrdemDeProducaoDto> Handle(RegistrarApontamentoCommand request, CancellationToken cancellationToken)
    {
        var ordem = await _repositorio.ObterPorIdAsync(request.OrdemDeServicoId, cancellationToken)
            ?? throw new KeyNotFoundException("Ordem de produção não encontrada.");

        ordem.RegistrarApontamento(request.MecanicoId, request.Texto, _timeProvider.GetUtcNow());

        await _repositorio.AtualizarAsync(ordem, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return OrdemDeProducaoMapper.ToDto(ordem);
    }
}
