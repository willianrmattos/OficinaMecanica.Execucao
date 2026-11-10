using MediatR;
using OficinaMecanica.Contracts.Events;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Application.Commands.FinalizarReparo;

public class FinalizarReparoCommandHandler : IRequestHandler<FinalizarReparoCommand, OrdemDeProducaoDto>
{
    private readonly IOrdemDeProducaoRepository _repositorio;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public FinalizarReparoCommandHandler(
        IOrdemDeProducaoRepository repositorio,
        IIntegrationEventPublisher publisher,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _repositorio = repositorio;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<OrdemDeProducaoDto> Handle(FinalizarReparoCommand request, CancellationToken cancellationToken)
    {
        var ordem = await _repositorio.ObterPorIdAsync(request.OrdemDeServicoId, cancellationToken)
            ?? throw new KeyNotFoundException("Ordem de produção não encontrada.");

        var agora = _timeProvider.GetUtcNow();

        ordem.FinalizarReparo(request.MecanicoId, request.Resumo, agora);

        await _repositorio.AtualizarAsync(ordem, cancellationToken);
        await _publisher.PublishAsync(
            new ExecucaoFinalizada(Guid.NewGuid(), ordem.Id, agora, ordem.Resumo!, agora),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return OrdemDeProducaoMapper.ToDto(ordem);
    }
}
