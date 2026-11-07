using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.Execucao.API.Extensions;
using OficinaMecanica.Execucao.API.Middleware;
using OficinaMecanica.Execucao.Application.Commands.ReivindicarProximoDiagnostico;
using OficinaMecanica.Execucao.Application.DTOs;

namespace OficinaMecanica.Execucao.API.Controllers;

[ApiController]
[Route("api/diagnosticos")]
[Authorize]
public class DiagnosticosController : ControllerBase
{
    private readonly IMediator _mediator;

    public DiagnosticosController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Reivindica a próxima ordem da fila de diagnóstico.</summary>
    /// <remarks>
    /// A fila é FIFO pela data de entrada. A ordem passa para <c>EmDiagnostico</c> e fica sob responsabilidade
    /// do mecânico do token (claim <c>sub</c>); o evento <c>DiagnosticoIniciado</c> é publicado na mesma
    /// transação. Dois mecânicos nunca recebem a mesma ordem.
    /// </remarks>
    /// <response code="200">Ordem reivindicada.</response>
    /// <response code="204">Fila de diagnóstico vazia.</response>
    /// <response code="401">Token ausente, inválido ou sem o claim <c>sub</c>.</response>
    /// <response code="409">Disputa pela fila não resolvida após as tentativas; repita a chamada.</response>
    [HttpPost("proximo")]
    [ProducesResponseType(typeof(OrdemDeProducaoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReivindicarProximo(CancellationToken cancellationToken)
    {
        var resultado = await _mediator.Send(new ReivindicarProximoDiagnosticoCommand(User.ObterIdDoMecanico()), cancellationToken);

        return resultado.FilaVazia ? NoContent() : Ok(resultado.Ordem);
    }
}
