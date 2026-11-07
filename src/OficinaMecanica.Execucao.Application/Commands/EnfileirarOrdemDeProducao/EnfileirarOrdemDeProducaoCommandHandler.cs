using MediatR;
using Microsoft.Extensions.Logging;
using OficinaMecanica.Execucao.Application.Common;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Application.Commands.EnfileirarOrdemDeProducao;

public class EnfileirarOrdemDeProducaoCommandHandler : IRequestHandler<EnfileirarOrdemDeProducaoCommand>
{
    private readonly IOrdemDeProducaoRepository _repositorio;
    private readonly ICancelamentoPrevioRepository _cancelamentoPrevioRepositorio;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EnfileirarOrdemDeProducaoCommandHandler> _logger;

    public EnfileirarOrdemDeProducaoCommandHandler(
        IOrdemDeProducaoRepository repositorio,
        ICancelamentoPrevioRepository cancelamentoPrevioRepositorio,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<EnfileirarOrdemDeProducaoCommandHandler> logger)
    {
        _repositorio = repositorio;
        _cancelamentoPrevioRepositorio = cancelamentoPrevioRepositorio;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Handle(EnfileirarOrdemDeProducaoCommand request, CancellationToken cancellationToken)
    {
        var existente = await _repositorio.ObterPorIdAsync(request.OrdemDeServicoId, cancellationToken);
        if (existente is not null)
        {
            _logger.LogInformation("Ordem {OrdemDeServicoId} já está enfileirada; reentrega ignorada.", request.OrdemDeServicoId);
            return;
        }

        // Optei por nao publicar nada quando o cancelamento chegou primeiro: nao existe evento de
        // resposta para "ordem nao enfileirada" e a saga ja deu essa producao por cancelada.
        var cancelamentoPrevio = await _cancelamentoPrevioRepositorio.ObterAsync(request.OrdemDeServicoId, cancellationToken);
        if (cancelamentoPrevio is not null)
        {
            _logger.LogInformation("Ordem {OrdemDeServicoId} foi cancelada antes de ser enfileirada; nada a enfileirar.", request.OrdemDeServicoId);
            return;
        }

        var ordem = OrdemDeProducao.Enfileirar(
            request.OrdemDeServicoId,
            request.Numero,
            VeiculoDaOrdem.Criar(request.Placa, request.Marca, request.Modelo),
            request.Servicos.ParaServicosDaOrdem(),
            request.Pecas.ParaPecasDaOrdem(),
            request.Observacoes,
            _timeProvider.GetUtcNow());

        // Duas entregas simultaneas da mesma mensagem passam juntas pela leitura acima; optei por
        // descartar sem erro quem perde a insercao.
        if (!await _repositorio.AdicionarAsync(ordem, cancellationToken))
        {
            _logger.LogInformation("Ordem {OrdemDeServicoId} foi enfileirada por outra entrega concorrente; descartando.", request.OrdemDeServicoId);
            return;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
