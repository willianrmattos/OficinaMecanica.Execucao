using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OficinaMecanica.Execucao.API.Health;
using OficinaMecanica.Execucao.Infrastructure.Messaging;

namespace OficinaMecanica.Execucao.Integration.Tests.Api;

// O corpo de /health/ready e anonimo: a excecao de um check traz host, porta, estado do cluster e credencial de
// connection string, e nunca pode ir para a resposta. Os asserts casam texto que as excecoes realmente contem.
public class ProntidaoResponseWriterTests
{
    private static async Task<(string Corpo, string TipoDeConteudo)> EscreverAsync(params (string Nome, HealthReportEntry Entrada)[] entradas)
    {
        var relatorio = new HealthReport(
            entradas.ToDictionary(e => e.Nome, e => e.Entrada), TimeSpan.Zero);
        var contexto = new DefaultHttpContext();
        contexto.Response.Body = new MemoryStream();

        await ProntidaoResponseWriter.EscreverAsync(contexto, relatorio);

        return (Encoding.UTF8.GetString(((MemoryStream)contexto.Response.Body).ToArray()), contexto.Response.ContentType!);
    }

    private static HealthReportEntry Entrada(HealthStatus status, string descricao, Exception? excecao) =>
        new(status, descricao, TimeSpan.Zero, excecao, new Dictionary<string, object>());

    [Fact]
    public async Task Writer_ComExcecaoNosChecks_NaoDeveVazarTipoMensagemHostNemCredencial()
    {
        var mongo = new TimeoutException("A timeout occurred selecting a server: mongodb://usuario:senha-do-banco@localhost:1 State: Disconnected");
        var rabbit = new InvalidOperationException("amqp://svc:segredo-do-broker@127.0.0.1:1 BrokerUnreachableException");

        var (corpo, _) = await EscreverAsync(
            ("mongodb", Entrada(HealthStatus.Unhealthy, "MongoDB indisponível.", mongo)),
            ("rabbitmq", Entrada(HealthStatus.Degraded, "Não foi possível conectar ao RabbitMQ.", rabbit)));

        corpo.Should().Contain("\"status\":\"Unhealthy\"").And.Contain("\"descricao\"");
        corpo.Should()
            .NotContain("TimeoutException").And.NotContain("InvalidOperationException")
            .And.NotContain("senha-do-banco").And.NotContain("segredo-do-broker")
            .And.NotContain("localhost:1").And.NotContain("127.0.0.1")
            .And.NotContain("Disconnected").And.NotContain("BrokerUnreachable");
    }

    [Fact]
    public async Task Writer_ComResultadoRealDoCheckDoRabbitMq_NaoDeveVazarAExcecaoDeConexao()
    {
        var check = new RabbitMqHealthCheck(Options.Create(new RabbitMqOptions { Host = "127.0.0.1", Port = 1, Username = "svc", Password = "x" }));
        var resultado = await check.CheckHealthAsync(new HealthCheckContext());
        resultado.Exception.Should().NotBeNull("a premissa do teste e que o resultado real carrega a excecao");

        var (corpo, _) = await EscreverAsync(
            ("rabbitmq", Entrada(resultado.Status, resultado.Description!, resultado.Exception)));

        corpo.Should().Contain("\"status\":\"Degraded\"");
        corpo.Should().NotContain(resultado.Exception!.GetType().Name)
            .And.NotContain(resultado.Exception.Message)
            .And.NotContain("127.0.0.1");
    }

    [Fact]
    public async Task Writer_DeveEscreverStatusAgregadoEDescricaoDeCadaCheckComAcentosLegiveis()
    {
        var (corpo, tipo) = await EscreverAsync(
            ("mongodb", Entrada(HealthStatus.Healthy, "Conexão com o MongoDB estabelecida.", null)),
            ("rabbitmq", Entrada(HealthStatus.Degraded, "Conexão AMQP com o RabbitMQ estabelecida.", null)));

        tipo.Should().Be("application/json; charset=utf-8");
        corpo.Should().Contain("Conexão AMQP com o RabbitMQ estabelecida.", "os acentos não são escapados em hexadecimal")
            .And.NotContain("\\u00");
        using var documento = JsonDocument.Parse(corpo);
        var raiz = documento.RootElement;
        raiz.GetProperty("status").GetString().Should().Be("Degraded");
        raiz.GetProperty("checks").GetProperty("mongodb").GetProperty("status").GetString().Should().Be("Healthy");
        raiz.GetProperty("checks").GetProperty("rabbitmq").GetProperty("descricao").GetString().Should().Be("Conexão AMQP com o RabbitMQ estabelecida.");
        raiz.GetProperty("checks").GetProperty("rabbitmq").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("status", "descricao");
    }
}
