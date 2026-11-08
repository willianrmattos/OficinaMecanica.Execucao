using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FluentValidation;
using OficinaMecanica.Execucao.Domain.Exceptions;

namespace OficinaMecanica.Execucao.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private const string RotaDesconhecida = "(rota desconhecida)";

    private static class TipoErro
    {
        public const string Negocio = "Erro de Negócio";
        public const string Validacao = "Erro de Validação";
        public const string NaoEncontrado = "Não Encontrado";
        public const string Conflito = "Conflito";
        public const string NaoAutorizado = "Não Autorizado";
        public const string Interno = "Erro Interno";
    }

    private static readonly JsonSerializerOptions OpcoesJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        // Quando o cliente fecha a conexão (timeout do chamador, do APIM ou do próprio usuário), o RequestAborted dispara e a
        // operação em andamento lança OperationCanceledException. Não há mais para quem responder, então não escrevo corpo e
        // não trato como erro do servidor: um ERR por timeout alheio geraria alerta sem haver defeito. Defino 499 (convenção
        // do nginx para "cliente fechou a requisição") para o log de acesso, a telemetria do OpenTelemetry e a métrica
        // http_requests_received_total do /metrics, que leem o status final, não contarem a chamada como sucesso; isso só
        // funciona porque o UseHttpMetrics é registrado antes deste middleware (no Program.cs) e enxerga o status depois do
        // meu tratamento. É inofensivo porque ninguém lê essa resposta. Cancelamento que não vem do RequestAborted (por
        // exemplo, o timeout interno de uma dependência) segue para o tratamento geral abaixo.
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested && !context.Response.HasStarted)
        {
            RegistrarRequisicaoAbandonada(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                TratarRespostaIniciada(context, ex);
                return;
            }

            await TratarAsync(context, ex);
        }
    }

    // Com a resposta já iniciada o status e os cabeçalhos estão congelados: no Kestrel, mexer neles lança
    // InvalidOperationException e a falha original se perderia dentro dela. Registro o erro e aborto a conexão: se eu só
    // retornasse, o Kestrel encerraria a resposta em chunks normalmente e o cliente receberia um 200 com o corpo truncado
    // como se estivesse completo; com o reset ele vê a falha. Quando foi o próprio cliente que fechou a conexão não há erro
    // do servidor nem o que abortar, então só registro a informação.
    private void TratarRespostaIniciada(HttpContext context, Exception exception)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Requisição cancelada pelo cliente com a resposta já iniciada: {Rota} (trace {TraceId}).",
                RotaDe(context),
                TraceIdDe(context));
            return;
        }

        _logger.LogError(exception, "Erro não tratado com a resposta já iniciada: {Message}", exception.Message);
        context.Abort();
    }

    private void RegistrarRequisicaoAbandonada(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;

        _logger.LogInformation(
            "Requisição cancelada pelo cliente antes da resposta: {Rota} (trace {TraceId}).",
            RotaDe(context),
            TraceIdDe(context));
    }

    // Optei por logar só o template da rota (por exemplo, api/ordens-de-producao/{id}) e o id do trace, nunca o caminho nem a
    // query: é segurança por construção. Nas rotas da Execução o caminho só leva o GUID da ordem, mas o log não depende
    // disso, e a query e os cabeçalhos podem carregar token.
    private static string RotaDe(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? RotaDesconhecida;

    private static string TraceIdDe(HttpContext context) =>
        Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

    private async Task TratarAsync(HttpContext context, Exception exception)
    {
        var resposta = context.Response;
        resposta.ContentType = "application/json";

        var erro = new ErrorResponse();

        switch (exception)
        {
            case DomainException domainEx:
                resposta.StatusCode = (int)HttpStatusCode.BadRequest;
                erro.Mensagem = domainEx.Message;
                erro.Tipo = TipoErro.Negocio;
                _logger.LogWarning("Erro de domínio: {Message}", domainEx.Message);
                break;

            case ValidationException validationEx:
                resposta.StatusCode = (int)HttpStatusCode.BadRequest;
                erro.Mensagem = "Erro de validação.";
                erro.Tipo = TipoErro.Validacao;
                erro.Erros = validationEx.Errors
                    .Select(e => new ErrorDetail(e.PropertyName, e.ErrorMessage))
                    .ToList();
                _logger.LogWarning("Erro de validação: {Errors}", string.Join(", ", validationEx.Errors.Select(e => e.ErrorMessage)));
                break;

            case KeyNotFoundException:
                resposta.StatusCode = (int)HttpStatusCode.NotFound;
                erro.Mensagem = "Ordem de produção não encontrada.";
                erro.Tipo = TipoErro.NaoEncontrado;
                break;

            // Conflito de Version ou de transacao que esgotou as tentativas: o cliente pode repetir a chamada.
            case ConcorrenciaException concorrenciaEx:
                resposta.StatusCode = (int)HttpStatusCode.Conflict;
                erro.Mensagem = concorrenciaEx.Message;
                erro.Tipo = TipoErro.Conflito;
                _logger.LogWarning("Conflito de concorrência: {Message}", concorrenciaEx.Message);
                break;

            case UnauthorizedAccessException unauthorizedEx:
                resposta.StatusCode = (int)HttpStatusCode.Unauthorized;
                erro.Mensagem = unauthorizedEx.Message;
                erro.Tipo = TipoErro.NaoAutorizado;
                break;

            default:
                resposta.StatusCode = (int)HttpStatusCode.InternalServerError;
                erro.Mensagem = "Ocorreu um erro interno no servidor.";
                erro.Tipo = TipoErro.Interno;
                _logger.LogError(exception, "Erro não tratado: {Message}", exception.Message);
                break;
        }

        await resposta.WriteAsync(JsonSerializer.Serialize(erro, OpcoesJson));
    }
}

public class ErrorResponse
{
    public string Tipo { get; set; } = string.Empty;
    public string Mensagem { get; set; } = string.Empty;
    public List<ErrorDetail>? Erros { get; set; }
}

public record ErrorDetail(string Campo, string Mensagem);
