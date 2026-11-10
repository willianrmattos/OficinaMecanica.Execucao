using MediatR;
using Microsoft.Extensions.Logging;
using OficinaMecanica.Contracts.Events;
using OficinaMecanica.Execucao.Application.Common;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Application.Commands.ReivindicarProximoDiagnostico;

public class ReivindicarProximoDiagnosticoCommandHandler : IRequestHandler<ReivindicarProximoDiagnosticoCommand, ResultadoDaReivindicacaoDto>
{
    private readonly IOrdemDeProducaoRepository _repositorio;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReivindicarProximoDiagnosticoCommandHandler> _logger;

    public ReivindicarProximoDiagnosticoCommandHandler(
        IOrdemDeProducaoRepository repositorio,
        IIntegrationEventPublisher publisher,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<ReivindicarProximoDiagnosticoCommandHandler> logger)
    {
        _repositorio = repositorio;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ResultadoDaReivindicacaoDto> Handle(ReivindicarProximoDiagnosticoCommand request, CancellationToken cancellationToken)
    {
        var agora = _timeProvider.GetUtcNow();

        var ordem = await ReivindicacaoDeFila.ReivindicarAsync(
            _repositorio,
            StatusProducao.Fila,
            o => o.IniciarDiagnostico(request.MecanicoId, agora),
            _logger,
            cancellationToken);

        if (ordem is null)
            return ResultadoDaReivindicacaoDto.Vazia();

        await _publisher.PublishAsync(
            new DiagnosticoIniciado(Guid.NewGuid(), ordem.Id, agora, request.MecanicoId),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ResultadoDaReivindicacaoDto.Reivindicada(OrdemDeProducaoMapper.ToDto(ordem));
    }
}
