using System.Net;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMecanica.Execucao.API.Middleware;
using OficinaMecanica.Execucao.Domain.Exceptions;

namespace OficinaMecanica.Execucao.Integration.Tests.Api;

public class ExceptionHandlingMiddlewareTests
{
    private static async Task<(int Status, ErrorResponse Corpo, string Bruto)> ExecutarAsync(Exception? excecao)
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => excecao is null ? Task.CompletedTask : throw excecao,
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var contexto = new DefaultHttpContext();
        contexto.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(contexto);

        contexto.Response.Body.Position = 0;
        var bruto = await new StreamReader(contexto.Response.Body).ReadToEndAsync();
        var corpo = bruto.Length == 0 ? new ErrorResponse() : JsonSerializer.Deserialize<ErrorResponse>(bruto, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return (contexto.Response.StatusCode, corpo, bruto);
    }

    [Fact]
    public async Task SemExcecao_DevePassarAdianteSemAlterarARespostaNemEscreverCorpo()
    {
        var (status, _, bruto) = await ExecutarAsync(null);

        status.Should().Be(StatusCodes.Status200OK);
        bruto.Should().BeEmpty();
    }

    [Fact]
    public async Task DomainException_DeveVirar400ComAMensagemDeNegocio()
    {
        var (status, corpo, _) = await ExecutarAsync(new DomainException("A ordem já está cancelada."));

        status.Should().Be((int)HttpStatusCode.BadRequest);
        corpo.Tipo.Should().Be("Erro de Negócio");
        corpo.Mensagem.Should().Be("A ordem já está cancelada.");
    }

    [Fact]
    public async Task ValidationException_DeveVirar400ComUmErroPorCampo()
    {
        var falhas = new[]
        {
            new ValidationFailure("Laudo", "O laudo é obrigatório."),
            new ValidationFailure("HorasEstimadas", "As horas devem ser maiores que zero.")
        };

        var (status, corpo, _) = await ExecutarAsync(new ValidationException(falhas));

        status.Should().Be((int)HttpStatusCode.BadRequest);
        corpo.Tipo.Should().Be("Erro de Validação");
        corpo.Erros.Should().BeEquivalentTo(
            new[] { new ErrorDetail("Laudo", "O laudo é obrigatório."), new ErrorDetail("HorasEstimadas", "As horas devem ser maiores que zero.") });
    }

    [Fact]
    public async Task KeyNotFoundException_DeveVirar404SemExporAMensagemOriginal()
    {
        var (status, corpo, bruto) = await ExecutarAsync(new KeyNotFoundException("chave interna 123"));

        status.Should().Be((int)HttpStatusCode.NotFound);
        corpo.Tipo.Should().Be("Não Encontrado");
        bruto.Should().NotContain("chave interna");
    }

    [Fact]
    public async Task ConcorrenciaException_DeveVirar409()
    {
        var (status, corpo, _) = await ExecutarAsync(new ConcorrenciaException("A ordem foi alterada por outra operação."));

        status.Should().Be((int)HttpStatusCode.Conflict);
        corpo.Tipo.Should().Be("Conflito");
        corpo.Mensagem.Should().Contain("alterada por outra operação");
    }

    [Fact]
    public async Task UnauthorizedAccessException_DeveVirar401()
    {
        var (status, corpo, _) = await ExecutarAsync(new UnauthorizedAccessException("Sem identificação."));

        status.Should().Be((int)HttpStatusCode.Unauthorized);
        corpo.Tipo.Should().Be("Não Autorizado");
    }

    [Fact]
    public async Task ExcecaoInesperada_DeveVirar500SemVazarDetalhes()
    {
        var (status, corpo, bruto) = await ExecutarAsync(new InvalidOperationException("string de conexão mongodb://usuario:senha@host"));

        status.Should().Be((int)HttpStatusCode.InternalServerError);
        corpo.Tipo.Should().Be("Erro Interno");
        corpo.Mensagem.Should().Be("Ocorreu um erro interno no servidor.");
        bruto.Should().NotContain("senha").And.NotContain("mongodb");
    }
}
