using MediatR;
using OficinaMecanica.Contracts.Events;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Application.Commands.InviabilizarExecucao;

public class InviabilizarExecucaoCommandHandler : IRequestHandler<InviabilizarExecucaoCommand, OrdemDeProducaoDto>
{
    private readonly IOrdemDeProducaoRepository _repositorio;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public InviabilizarExecucaoCommandHandler(
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

    public async Task<OrdemDeProducaoDto> Handle(InviabilizarExecucaoCommand request, CancellationToken cancellationToken)
    {
        var ordem = await _repositorio.ObterPorIdAsync(request.OrdemDeServicoId, cancellationToken)
            ?? throw new KeyNotFoundException("Ordem de produção não encontrada.");

        var agora = _timeProvider.GetUtcNow();

        ordem.Inviabilizar(request.MecanicoId, request.Motivo, agora);

        await _repositorio.AtualizarAsync(ordem, cancellationToken);
        await _publisher.PublishAsync(
            new ExecucaoInviavel(Guid.NewGuid(), ordem.Id, agora, ordem.MotivoInviabilidade!),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return OrdemDeProducaoMapper.ToDto(ordem);
    }
}
