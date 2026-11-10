using MediatR;
using OficinaMecanica.Contracts.Events;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Application.Commands.ConcluirDiagnostico;

public class ConcluirDiagnosticoCommandHandler : IRequestHandler<ConcluirDiagnosticoCommand, OrdemDeProducaoDto>
{
    private readonly IOrdemDeProducaoRepository _repositorio;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public ConcluirDiagnosticoCommandHandler(
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

    public async Task<OrdemDeProducaoDto> Handle(ConcluirDiagnosticoCommand request, CancellationToken cancellationToken)
    {
        var ordem = await _repositorio.ObterPorIdAsync(request.OrdemDeServicoId, cancellationToken)
            ?? throw new KeyNotFoundException("Ordem de produção não encontrada.");

        var agora = _timeProvider.GetUtcNow();
        var laudo = Laudo.Criar(request.Laudo, request.HorasEstimadas);

        ordem.ConcluirDiagnostico(request.MecanicoId, laudo, agora);

        await _repositorio.AtualizarAsync(ordem, cancellationToken);
        await _publisher.PublishAsync(
            new DiagnosticoConcluido(Guid.NewGuid(), ordem.Id, agora, request.MecanicoId, laudo.Texto),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return OrdemDeProducaoMapper.ToDto(ordem);
    }
}
