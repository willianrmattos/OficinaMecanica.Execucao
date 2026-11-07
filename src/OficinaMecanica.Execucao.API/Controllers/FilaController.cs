using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.Execucao.API.Middleware;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Application.Queries.ListarFila;
using OficinaMecanica.Execucao.Application.Queries.ObterResumoDaFila;
using OficinaMecanica.Execucao.Domain.Enums;

namespace OficinaMecanica.Execucao.API.Controllers;

[ApiController]
[Route("api/fila")]
[Authorize]
public class FilaController : ControllerBase
{
    private readonly IMediator _mediator;

    public FilaController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Lista as ordens de produção com filtros e paginação.</summary>
    /// <remarks>
    /// Com filtro de status a ordem reflete a ordem de consumo (data de entrada na fila de diagnóstico,
    /// data de liberação na fila de reparo); sem filtro, pela data de entrada.
    /// </remarks>
    /// <param name="status">Filtra por status (por exemplo <c>Fila</c>, <c>EmDiagnostico</c>, <c>AguardandoExecucao</c>).</param>
    /// <param name="mecanicoId">Filtra pelo mecânico responsável (id opaco do token).</param>
    /// <param name="pagina">Página, a partir de 1.</param>
    /// <param name="tamanho">Itens por página, de 1 a 100.</param>
    /// <param name="cancellationToken">Cancelamento da requisição.</param>
    /// <response code="200">Página de ordens.</response>
    /// <response code="400">Parâmetros inválidos.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PaginacaoResultDto<OrdemDeProducaoResumoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Listar(
        [FromQuery] StatusProducao? status,
        [FromQuery] string? mecanicoId,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 10,
        CancellationToken cancellationToken = default)
    {
        var resultado = await _mediator.Send(new ListarFilaQuery(status, mecanicoId, pagina, tamanho), cancellationToken);

        return Ok(resultado);
    }

    /// <summary>Resumo da fila: quantidade de ordens em cada status.</summary>
    /// <param name="cancellationToken">Cancelamento da requisição.</param>
    /// <response code="200">Contagem por status (todos os status, mesmo com zero) e total.</response>
    [HttpGet("resumo")]
    [ProducesResponseType(typeof(ResumoDaFilaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Resumo(CancellationToken cancellationToken)
    {
        var resumo = await _mediator.Send(new ObterResumoDaFilaQuery(), cancellationToken);

        return Ok(resumo);
    }
}
