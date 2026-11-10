using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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

    // Cliente que fecha a conexão (timeout do OS, do APIM ou do usuário): o RequestAborted dispara e o handler em
    // andamento lança OperationCanceledException. Os testes abaixo fixam o que é abandono e o que continua erro.
    private static DefaultHttpContext NovoContexto(CancellationToken requisicaoAbortada = default)
    {
        var contexto = new DefaultHttpContext { RequestAborted = requisicaoAbortada };
        contexto.Response.Body = new MemoryStream();
        return contexto;
    }

    private static Exception Cancelamento(string tipo, CancellationToken token) => tipo == nameof(TaskCanceledException)
        ? new TaskCanceledException("A tarefa foi cancelada.", null, token)
        : new OperationCanceledException("The operation was canceled.", token);

    private static List<(LogLevel Nivel, string Mensagem, Exception? Excecao)> Registros(Mock<ILogger<ExceptionHandlingMiddleware>> logger) =>
        logger.Invocations
            .Where(i => i.Method.Name == nameof(ILogger.Log))
            .Select(i => ((LogLevel)i.Arguments[0], i.Arguments[2].ToString()!, (Exception?)i.Arguments[3]))
            .ToList();

    private static string CorpoEscrito(DefaultHttpContext contexto)
    {
        contexto.Response.Body.Position = 0;
        return new StreamReader(contexto.Response.Body).ReadToEnd();
    }

    // Reproduz o Kestrel com a resposta em andamento: status e cabeçalhos ficam congelados e qualquer mudança lança
    // InvalidOperationException, como acontece no servidor real (o DefaultHttpContext sozinho aceitaria a mudança em silêncio).
    private sealed class RespostaJaIniciada : IHttpResponseFeature
    {
        public int StatusCode
        {
            get => StatusCodes.Status200OK;
            set => throw new InvalidOperationException("StatusCode cannot be set because the response has already started.");
        }

        public string? ReasonPhrase
        {
            get => null;
            set => throw new InvalidOperationException("ReasonPhrase cannot be set because the response has already started.");
        }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary { IsReadOnly = true };

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }

    // Conexão de teste: Abort() cancela o RequestAborted (como o Kestrel faz) e fica registrado; o cliente que fecha por conta
    // própria cancela o mesmo token sem passar pelo Abort(). O feature padrão do DefaultHttpContext não faz nada no Abort() e não cancela o RequestAborted.
    private sealed class ConexaoQueRegistraAbort : IHttpRequestLifetimeFeature
    {
        private readonly CancellationTokenSource _fonte = new();

        public CancellationToken RequestAborted
        {
            get => _fonte.Token;
            set => throw new NotSupportedException();
        }

        public int Abortes { get; private set; }

        public void Abort()
        {
            Abortes++;
            _fonte.Cancel();
        }

        public void ClienteFechou() => _fonte.Cancel();
    }

    private static void DefinirRota(HttpContext contexto, string template) =>
        contexto.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(template), 0, EndpointMetadataCollection.Empty, "teste"));

    [Theory]
    [InlineData(nameof(OperationCanceledException))]
    [InlineData(nameof(TaskCanceledException))]
    public async Task ClienteQueFechouAConexao_NaoDeveEscreverCorpoNemLogarErro(string tipo)
    {
        using var abortada = new CancellationTokenSource();
        var logger = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var contexto = NovoContexto(abortada.Token);
        DefinirRota(contexto, "api/fila/{documento}");
        contexto.Request.Path = "/api/fila/12345678901";
        contexto.Request.QueryString = new QueryString("?access_token=segredo-da-query");
        contexto.Request.Headers.Authorization = "Bearer segredo-do-cabecalho";
        var middleware = new ExceptionHandlingMiddleware(
            _ =>
            {
                abortada.Cancel();
                throw Cancelamento(tipo, abortada.Token);
            },
            logger.Object);

        var acao = () => middleware.InvokeAsync(contexto);

        await acao.Should().NotThrowAsync();
        CorpoEscrito(contexto).Should().BeEmpty();
        contexto.Response.ContentType.Should().BeNull();
        contexto.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
        var registros = Registros(logger);
        registros.Should().NotContain(r => r.Nivel >= LogLevel.Warning);
        registros.Should().ContainSingle(r => r.Nivel == LogLevel.Information)
            .Which.Mensagem.Should().Contain("api/fila/{documento}")
            .And.NotContain("12345678901")
            .And.NotContain("segredo");
        registros.Should().OnlyContain(r => r.Excecao == null);
    }

    [Fact]
    public async Task ClienteQueFechouAConexao_SemEndpointDeRota_DeveLogarRotaDesconhecidaSemOCaminho()
    {
        using var abortada = new CancellationTokenSource();
        var logger = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var contexto = NovoContexto(abortada.Token);
        contexto.Request.Path = "/api/qualquer/12345678901";
        contexto.Request.QueryString = new QueryString("?cpf=12345678901");
        var middleware = new ExceptionHandlingMiddleware(
            _ =>
            {
                abortada.Cancel();
                throw new OperationCanceledException(abortada.Token);
            },
            logger.Object);

        await middleware.InvokeAsync(contexto);

        contexto.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
        Registros(logger).Should().ContainSingle(r => r.Nivel == LogLevel.Information)
            .Which.Mensagem.Should().Contain("(rota desconhecida)")
            .And.NotContain("12345678901")
            .And.NotContain("/api/qualquer");
    }

    [Fact]
    public async Task ClienteQueFechouAConexao_ComEndpointQueNaoEDeRota_DeveLogarRotaDesconhecidaSemOCaminho()
    {
        using var abortada = new CancellationTokenSource();
        var logger = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var contexto = NovoContexto(abortada.Token);
        contexto.Request.Path = "/api/qualquer/12345678901";
        contexto.SetEndpoint(new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "sem-padrao-de-rota"));
        var middleware = new ExceptionHandlingMiddleware(
            _ =>
            {
                abortada.Cancel();
                throw new OperationCanceledException(abortada.Token);
            },
            logger.Object);

        await middleware.InvokeAsync(contexto);

        Registros(logger).Should().ContainSingle(r => r.Nivel == LogLevel.Information)
            .Which.Mensagem.Should().Contain("(rota desconhecida)")
            .And.NotContain("12345678901");
    }

    [Fact]
    public async Task ClienteQueFechouAConexao_DeveLogarOTraceIdDaAtividadeCorrente()
    {
        using var atividade = new Activity("teste").Start();
        using var abortada = new CancellationTokenSource();
        var logger = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var middleware = new ExceptionHandlingMiddleware(
            _ =>
            {
                abortada.Cancel();
                throw new OperationCanceledException(abortada.Token);
            },
            logger.Object);

        await middleware.InvokeAsync(NovoContexto(abortada.Token));

        Registros(logger).Should().ContainSingle().Which.Mensagem.Should().Contain(atividade.TraceId.ToString());
    }

    [Fact]
    public async Task ClienteQueFechouAConexao_SemAtividade_DeveLogarOIdDaRequisicao()
    {
        Activity.Current = null;
        using var abortada = new CancellationTokenSource();
        var logger = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var contexto = NovoContexto(abortada.Token);
        contexto.TraceIdentifier = "requisicao-123";
        var middleware = new ExceptionHandlingMiddleware(
            _ =>
            {
                abortada.Cancel();
                throw new OperationCanceledException(abortada.Token);
            },
            logger.Object);

        await middleware.InvokeAsync(contexto);

        Registros(logger).Should().ContainSingle().Which.Mensagem.Should().Contain("requisicao-123");
    }

    [Theory]
    [InlineData(nameof(OperationCanceledException))]
    [InlineData(nameof(TaskCanceledException))]
    public async Task CancelamentoComClienteAindaConectado_DeveContinuarVirando500ComErroLogado(string tipo)
    {
        // Timeout interno de uma dependência: o cliente segue esperando e precisa receber a resposta de erro.
        var logger = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var contexto = NovoContexto(CancellationToken.None);
        var excecao = Cancelamento(tipo, CancellationToken.None);
        var middleware = new ExceptionHandlingMiddleware(_ => throw excecao, logger.Object);

        await middleware.InvokeAsync(contexto);

        contexto.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        CorpoEscrito(contexto).Should().Contain("Erro Interno");
        Registros(logger).Should().ContainSingle(r => r.Nivel == LogLevel.Error)
            .Which.Excecao.Should().BeSameAs(excecao);
    }

    [Fact]
    public async Task ExcecaoQueNaoEhCancelamento_ComClienteQueFechouAConexao_DeveContinuarVirando500ComErroLogado()
    {
        using var abortada = new CancellationTokenSource();
        abortada.Cancel();
        var logger = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var contexto = NovoContexto(abortada.Token);
        var excecao = new InvalidOperationException("falha real");
        var middleware = new ExceptionHandlingMiddleware(_ => throw excecao, logger.Object);

        await middleware.InvokeAsync(contexto);

        contexto.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        CorpoEscrito(contexto).Should().Contain("Erro Interno");
        Registros(logger).Should().ContainSingle(r => r.Nivel == LogLevel.Error)
            .Which.Excecao.Should().BeSameAs(excecao);
    }

    [Fact]
    public async Task DomainException_ComClienteQueFechouAConexao_DeveContinuarVirando400()
    {
        using var abortada = new CancellationTokenSource();
        abortada.Cancel();
        var logger = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var contexto = NovoContexto(abortada.Token);
        var middleware = new ExceptionHandlingMiddleware(_ => throw new DomainException("A ordem já está cancelada."), logger.Object);

        await middleware.InvokeAsync(contexto);

        contexto.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        JsonSerializer.Deserialize<ErrorResponse>(CorpoEscrito(contexto), new JsonSerializerOptions(JsonSerializerDefaults.Web))!
            .Tipo.Should().Be("Erro de Negócio");
        Registros(logger).Should().ContainSingle().Which.Nivel.Should().Be(LogLevel.Warning);
    }

    [Theory]
    [InlineData(nameof(OperationCanceledException), false)]
    [InlineData(nameof(DomainException), false)]
    [InlineData(nameof(DomainException), true)]
    [InlineData(nameof(InvalidOperationException), false)]
    [InlineData(nameof(InvalidOperationException), true)]
    public async Task ExcecaoComRespostaJaIniciada_NaoDeveTocarNaRespostaRegistrarOErroEAbortarAConexao(string tipo, bool clienteFechou)
    {
        // Com a resposta em andamento (e, no Kestrel, status e cabeçalhos congelados) tentar escrever o 500 lançaria
        // InvalidOperationException e esconderia a falha original; só retornar deixaria o cliente com um 200 truncado. O
        // middleware registra o erro e aborta a conexão. OperationCanceledException sem o cliente ter fechado é, por
        // exemplo, o timeout interno de uma dependência.
        var conexao = new ConexaoQueRegistraAbort();
        var logger = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var contexto = NovoContexto();
        contexto.Features.Set<IHttpResponseFeature>(new RespostaJaIniciada());
        contexto.Features.Set<IHttpRequestLifetimeFeature>(conexao);
        Exception excecao = tipo switch
        {
            nameof(OperationCanceledException) => new OperationCanceledException("timeout interno da dependência"),
            nameof(DomainException) => new DomainException("A ordem já está cancelada."),
            _ => new InvalidOperationException("falha real")
        };
        var middleware = new ExceptionHandlingMiddleware(
            _ =>
            {
                if (clienteFechou)
                    conexao.ClienteFechou();

                throw excecao;
            },
            logger.Object);

        var acao = () => middleware.InvokeAsync(contexto);

        await acao.Should().NotThrowAsync();
        contexto.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        contexto.Response.ContentType.Should().BeNull();
        CorpoEscrito(contexto).Should().BeEmpty();
        conexao.Abortes.Should().Be(1);
        contexto.RequestAborted.IsCancellationRequested.Should().BeTrue();
        var registros = Registros(logger);
        registros.Should().NotContain(r => r.Nivel == LogLevel.Information);
        registros.Should().ContainSingle(r => r.Nivel == LogLevel.Error).Which.Excecao.Should().BeSameAs(excecao);
    }

    [Fact]
    public async Task ClienteQueFechouAConexaoComRespostaJaIniciada_DeveLogarInformationSemAbortarNemMexerNaResposta()
    {
        // A conexão já está fechada: não há erro do servidor nem o que abortar.
        var conexao = new ConexaoQueRegistraAbort();
        var logger = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var contexto = NovoContexto();
        contexto.Features.Set<IHttpResponseFeature>(new RespostaJaIniciada());
        contexto.Features.Set<IHttpRequestLifetimeFeature>(conexao);
        contexto.Request.Path = "/api/fila/12345678901";
        contexto.Request.QueryString = new QueryString("?access_token=segredo-da-query");
        DefinirRota(contexto, "api/fila/{documento}");
        var middleware = new ExceptionHandlingMiddleware(
            _ =>
            {
                conexao.ClienteFechou();
                throw new OperationCanceledException(conexao.RequestAborted);
            },
            logger.Object);

        var acao = () => middleware.InvokeAsync(contexto);

        await acao.Should().NotThrowAsync();
        contexto.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        contexto.Response.ContentType.Should().BeNull();
        CorpoEscrito(contexto).Should().BeEmpty();
        conexao.Abortes.Should().Be(0);
        var registros = Registros(logger);
        registros.Should().NotContain(r => r.Nivel >= LogLevel.Warning);
        registros.Should().ContainSingle(r => r.Nivel == LogLevel.Information)
            .Which.Mensagem.Should().Contain("api/fila/{documento}")
            .And.NotContain("12345678901")
            .And.NotContain("segredo");
    }
}
