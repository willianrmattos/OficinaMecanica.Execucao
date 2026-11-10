using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.Execucao.API.Extensions;
using OficinaMecanica.Execucao.API.Middleware;
using OficinaMecanica.Execucao.Application.Commands.ReivindicarProximoReparo;
using OficinaMecanica.Execucao.Application.DTOs;

namespace OficinaMecanica.Execucao.API.Controllers;

[ApiController]
[Route("api/reparos")]
[Authorize]
public class ReparosController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReparosController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Reivindica a próxima ordem liberada para reparo.</summary>
    /// <remarks>
    /// A fila é FIFO pelo momento em que a ordem foi liberada (pagamento aprovado e diagnóstico concluído),
    /// não pela data de entrada. A ordem passa para <c>EmExecucao</c> e o mecânico do token (claim
    /// <c>sub</c>) vira o responsável.
    /// </remarks>
    /// <response code="200">Ordem reivindicada.</response>
    /// <response code="204">Fila de reparo vazia.</response>
    /// <response code="401">Token ausente, inválido ou sem o claim <c>sub</c>.</response>
    /// <response code="409">Disputa pela fila não resolvida após as tentativas; repita a chamada.</response>
    [HttpPost("proximo")]
    [ProducesResponseType(typeof(OrdemDeProducaoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReivindicarProximo(CancellationToken cancellationToken)
    {
        var resultado = await _mediator.Send(new ReivindicarProximoReparoCommand(User.ObterIdDoMecanico()), cancellationToken);

        return resultado.FilaVazia ? NoContent() : Ok(resultado.Ordem);
    }
}
