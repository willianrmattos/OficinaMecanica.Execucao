using MediatR;
using Microsoft.Extensions.Logging;
using OficinaMecanica.Execucao.Application.Common;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Application.Commands.LiberarParaExecucao;

public class LiberarParaExecucaoCommandHandler : IRequestHandler<LiberarParaExecucaoCommand>
{
    private readonly IOrdemDeProducaoRepository _repositorio;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LiberarParaExecucaoCommandHandler> _logger;

    public LiberarParaExecucaoCommandHandler(
        IOrdemDeProducaoRepository repositorio,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<LiberarParaExecucaoCommandHandler> logger)
    {
        _repositorio = repositorio;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Handle(LiberarParaExecucaoCommand request, CancellationToken cancellationToken)
    {
        var ordem = await _repositorio.ObterPorIdAsync(request.OrdemDeServicoId, cancellationToken)
            ?? throw new KeyNotFoundException("Ordem de produção não encontrada.");

        var liberada = ordem.LiberarParaExecucao(
            request.Servicos.ParaServicosDaOrdem(),
            request.Pecas.ParaPecasDaOrdem(),
            _timeProvider.GetUtcNow());

        if (!liberada)
        {
            _logger.LogInformation("Ordem {OrdemDeServicoId} já está liberada para execução; reentrega ignorada.", request.OrdemDeServicoId);
            return;
        }

        await _repositorio.AtualizarAsync(ordem, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
