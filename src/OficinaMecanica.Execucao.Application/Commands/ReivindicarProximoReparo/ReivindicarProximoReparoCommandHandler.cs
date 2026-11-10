using MediatR;
using Microsoft.Extensions.Logging;
using OficinaMecanica.Execucao.Application.Common;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Application.Commands.ReivindicarProximoReparo;

// Optei por nao publicar nada ao iniciar o reparo: a saga ja considera a OS "em execucao" desde o
// pagamento aprovado, entao so o fim (finalizada ou inviavel) interessa a ela.
public class ReivindicarProximoReparoCommandHandler : IRequestHandler<ReivindicarProximoReparoCommand, ResultadoDaReivindicacaoDto>
{
    private readonly IOrdemDeProducaoRepository _repositorio;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReivindicarProximoReparoCommandHandler> _logger;

    public ReivindicarProximoReparoCommandHandler(
        IOrdemDeProducaoRepository repositorio,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<ReivindicarProximoReparoCommandHandler> logger)
    {
        _repositorio = repositorio;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ResultadoDaReivindicacaoDto> Handle(ReivindicarProximoReparoCommand request, CancellationToken cancellationToken)
    {
        var agora = _timeProvider.GetUtcNow();

        var ordem = await ReivindicacaoDeFila.ReivindicarAsync(
            _repositorio,
            StatusProducao.AguardandoExecucao,
            o => o.IniciarReparo(request.MecanicoId, agora),
            _logger,
            cancellationToken);

        if (ordem is null)
            return ResultadoDaReivindicacaoDto.Vazia();

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ResultadoDaReivindicacaoDto.Reivindicada(OrdemDeProducaoMapper.ToDto(ordem));
    }
}
