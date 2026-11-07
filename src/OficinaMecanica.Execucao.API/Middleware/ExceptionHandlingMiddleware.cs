using System.Net;
using System.Text.Json;
using FluentValidation;
using OficinaMecanica.Execucao.Domain.Exceptions;

namespace OficinaMecanica.Execucao.API.Middleware;

public class ExceptionHandlingMiddleware
{
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
        catch (Exception ex)
        {
            await TratarAsync(context, ex);
        }
    }

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
