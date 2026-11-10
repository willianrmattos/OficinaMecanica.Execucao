using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.Execucao.API.Extensions;
using OficinaMecanica.Execucao.API.Middleware;
using OficinaMecanica.Execucao.API.Requests;
using OficinaMecanica.Execucao.Application.Commands.ConcluirDiagnostico;
using OficinaMecanica.Execucao.Application.Commands.FinalizarReparo;
using OficinaMecanica.Execucao.Application.Commands.InviabilizarExecucao;
using OficinaMecanica.Execucao.Application.Commands.RegistrarApontamento;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Application.Queries.ObterOrdemDeProducao;

namespace OficinaMecanica.Execucao.API.Controllers;

[ApiController]
[Route("api/ordens-de-producao")]
[Authorize]
public class OrdensDeProducaoController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdensDeProducaoController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Consulta uma ordem de produção pelo id da ordem de serviço.</summary>
    /// <remarks>Usado pela consulta síncrona detalhada do OS Service; somente leitura.</remarks>
    /// <param name="id">Id da ordem de serviço (o mesmo da ordem de produção).</param>
    /// <param name="cancellationToken">Cancelamento da requisição.</param>
    /// <response code="200">Ordem encontrada.</response>
    /// <response code="404">Não existe ordem de produção com esse id.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrdemDeProducaoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken cancellationToken)
    {
        var ordem = await _mediator.Send(new ObterOrdemDeProducaoPorIdQuery(id), cancellationToken);

        return ordem is null
            ? NotFound(new ErrorResponse { Tipo = "Não Encontrado", Mensagem = "Ordem de produção não encontrada." })
            : Ok(ordem);
    }

    /// <summary>Registra um apontamento do mecânico responsável.</summary>
    /// <remarks>Vale com a ordem em diagnóstico ou em execução, só para o mecânico responsável (claim <c>sub</c>).</remarks>
    /// <param name="id">Id da ordem de serviço.</param>
    /// <param name="request">Texto do apontamento.</param>
    /// <param name="cancellationToken">Cancelamento da requisição.</param>
    /// <response code="200">Apontamento registrado; devolve a ordem atualizada.</response>
    /// <response code="400">Validação ou regra de negócio (status inválido, mecânico não responsável).</response>
    /// <response code="404">Ordem não encontrada.</response>
    /// <response code="409">A ordem foi alterada por outra operação; repita a chamada.</response>
    [HttpPost("{id:guid}/apontamentos")]
    [ProducesResponseType(typeof(OrdemDeProducaoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegistrarApontamento(Guid id, [FromBody] RegistrarApontamentoRequest request, CancellationToken cancellationToken)
    {
        var ordem = await _mediator.Send(new RegistrarApontamentoCommand(id, User.ObterIdDoMecanico(), request.Texto), cancellationToken);

        return Ok(ordem);
    }

    /// <summary>Conclui o diagnóstico com o laudo.</summary>
    /// <remarks>
    /// Só o mecânico responsável conclui. O evento <c>DiagnosticoConcluido</c> (com o laudo) é publicado na
    /// mesma transação e a saga do OS Service gera o orçamento.
    /// </remarks>
    /// <param name="id">Id da ordem de serviço.</param>
    /// <param name="request">Laudo e horas estimadas.</param>
    /// <param name="cancellationToken">Cancelamento da requisição.</param>
    /// <response code="200">Diagnóstico concluído; devolve a ordem atualizada.</response>
    /// <response code="400">Validação ou regra de negócio.</response>
    /// <response code="404">Ordem não encontrada.</response>
    /// <response code="409">A ordem foi alterada por outra operação; repita a chamada.</response>
    [HttpPost("{id:guid}/diagnostico/concluir")]
    [ProducesResponseType(typeof(OrdemDeProducaoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConcluirDiagnostico(Guid id, [FromBody] ConcluirDiagnosticoRequest request, CancellationToken cancellationToken)
    {
        var ordem = await _mediator.Send(
            new ConcluirDiagnosticoCommand(id, User.ObterIdDoMecanico(), request.Laudo, request.HorasEstimadas), cancellationToken);

        return Ok(ordem);
    }

    /// <summary>Finaliza o reparo.</summary>
    /// <remarks>Só o mecânico responsável finaliza. Publica <c>ExecucaoFinalizada</c> na mesma transação.</remarks>
    /// <param name="id">Id da ordem de serviço.</param>
    /// <param name="request">Resumo do que foi executado.</param>
    /// <param name="cancellationToken">Cancelamento da requisição.</param>
    /// <response code="200">Reparo finalizado; devolve a ordem atualizada.</response>
    /// <response code="400">Validação ou regra de negócio.</response>
    /// <response code="404">Ordem não encontrada.</response>
    /// <response code="409">A ordem foi alterada por outra operação; repita a chamada.</response>
    [HttpPost("{id:guid}/reparo/finalizar")]
    [ProducesResponseType(typeof(OrdemDeProducaoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> FinalizarReparo(Guid id, [FromBody] FinalizarReparoRequest request, CancellationToken cancellationToken)
    {
        var ordem = await _mediator.Send(new FinalizarReparoCommand(id, User.ObterIdDoMecanico(), request.Resumo), cancellationToken);

        return Ok(ordem);
    }

    /// <summary>Marca a execução como inviável.</summary>
    /// <remarks>
    /// Vale com a ordem aguardando execução ou em execução; em execução, só o mecânico responsável.
    /// Publica <c>ExecucaoInviavel</c> na mesma transação e a saga estorna o pagamento.
    /// </remarks>
    /// <param name="id">Id da ordem de serviço.</param>
    /// <param name="request">Motivo da inviabilidade.</param>
    /// <param name="cancellationToken">Cancelamento da requisição.</param>
    /// <response code="200">Execução inviabilizada; devolve a ordem atualizada.</response>
    /// <response code="400">Validação ou regra de negócio.</response>
    /// <response code="404">Ordem não encontrada.</response>
    /// <response code="409">A ordem foi alterada por outra operação; repita a chamada.</response>
    [HttpPost("{id:guid}/reparo/inviabilizar")]
    [ProducesResponseType(typeof(OrdemDeProducaoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> InviabilizarReparo(Guid id, [FromBody] InviabilizarReparoRequest request, CancellationToken cancellationToken)
    {
        var ordem = await _mediator.Send(new InviabilizarExecucaoCommand(id, User.ObterIdDoMecanico(), request.Motivo), cancellationToken);

        return Ok(ordem);
    }
}
