using MediatR;
using Microsoft.Extensions.Logging;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Application.Commands.CancelarProducao;

public class CancelarProducaoCommandHandler : IRequestHandler<CancelarProducaoCommand>
{
    private readonly IOrdemDeProducaoRepository _repositorio;
    private readonly ICancelamentoPrevioRepository _cancelamentoPrevioRepositorio;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CancelarProducaoCommandHandler> _logger;

    public CancelarProducaoCommandHandler(
        IOrdemDeProducaoRepository repositorio,
        ICancelamentoPrevioRepository cancelamentoPrevioRepositorio,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<CancelarProducaoCommandHandler> logger)
    {
        _repositorio = repositorio;
        _cancelamentoPrevioRepositorio = cancelamentoPrevioRepositorio;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Handle(CancelarProducaoCommand request, CancellationToken cancellationToken)
    {
        var ordem = await _repositorio.ObterPorIdAsync(request.OrdemDeServicoId, cancellationToken);

        // Optei por tratar ordem inexistente como "cancelar antes de criar": o cancelamento e
        // compensacao da saga e pode ultrapassar o SolicitarDiagnostico, entao registro um tombstone
        // para o enfileiramento atrasado nao criar o que ja foi mandado desfazer (e a mensagem nao
        // vai para a fila de erro).
        if (ordem is null)
        {
            await RegistrarCancelamentoPrevioAsync(request, cancellationToken);
            return;
        }

        if (!ordem.Cancelar(request.Motivo, _timeProvider.GetUtcNow()))
        {
            _logger.LogInformation("Ordem {OrdemDeServicoId} já está cancelada; reentrega ignorada.", request.OrdemDeServicoId);
            return;
        }

        await _repositorio.AtualizarAsync(ordem, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task RegistrarCancelamentoPrevioAsync(CancelarProducaoCommand request, CancellationToken cancellationToken)
    {
        var existente = await _cancelamentoPrevioRepositorio.ObterAsync(request.OrdemDeServicoId, cancellationToken);
        if (existente is not null)
        {
            _logger.LogInformation("Cancelamento prévio da ordem {OrdemDeServicoId} já registrado; reentrega ignorada.", request.OrdemDeServicoId);
            return;
        }

        var cancelamento = new CancelamentoPrevio(request.OrdemDeServicoId, request.Motivo, _timeProvider.GetUtcNow());
        if (!await _cancelamentoPrevioRepositorio.AdicionarAsync(cancelamento, cancellationToken))
        {
            _logger.LogInformation("Cancelamento prévio da ordem {OrdemDeServicoId} registrado por outra entrega concorrente; descartando.", request.OrdemDeServicoId);
            return;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
