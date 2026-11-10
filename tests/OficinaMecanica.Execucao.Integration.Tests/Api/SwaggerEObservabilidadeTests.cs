using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace OficinaMecanica.Execucao.Integration.Tests.Api;

public class SwaggerEObservabilidadeTests : ApiTestBase
{
    private async Task<JsonElement> ObterSwaggerAsync(Action<HttpRequestMessage>? configurar = null)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Get, "/swagger/v1/swagger.json");
        configurar?.Invoke(requisicao);
        var resposta = await Api.CreateClient().SendAsync(requisicao);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task Swagger_DeveDocumentarTodosOsEndpointsComOperationIdControllerAction()
    {
        var swagger = await ObterSwaggerAsync();

        var operacoes = swagger.GetProperty("paths").EnumerateObject()
            .SelectMany(caminho => caminho.Value.EnumerateObject()
                .Select(metodo => (Rota: caminho.Name, Metodo: metodo.Name, Id: metodo.Value.GetProperty("operationId").GetString())))
            .ToList();

        operacoes.Should().BeEquivalentTo(new[]
        {
            ("/api/diagnosticos/proximo", "post", "Diagnosticos_ReivindicarProximo"),
            ("/api/reparos/proximo", "post", "Reparos_ReivindicarProximo"),
            ("/api/ordens-de-producao/{id}", "get", "OrdensDeProducao_ObterPorId"),
            ("/api/ordens-de-producao/{id}/apontamentos", "post", "OrdensDeProducao_RegistrarApontamento"),
            ("/api/ordens-de-producao/{id}/diagnostico/concluir", "post", "OrdensDeProducao_ConcluirDiagnostico"),
            ("/api/ordens-de-producao/{id}/reparo/finalizar", "post", "OrdensDeProducao_FinalizarReparo"),
            ("/api/ordens-de-producao/{id}/reparo/inviabilizar", "post", "OrdensDeProducao_InviabilizarReparo"),
            ("/api/fila", "get", "Fila_Listar"),
            ("/api/fila/resumo", "get", "Fila_Resumo")
        });
    }

    [Fact]
    public async Task Swagger_DeveDeclararOEsquemaBearerEDescreverAsRespostasEmPortugues()
    {
        var swagger = await ObterSwaggerAsync();

        swagger.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _).Should().BeTrue();
        var reivindicar = swagger.GetProperty("paths").GetProperty("/api/diagnosticos/proximo").GetProperty("post");
        reivindicar.GetProperty("summary").GetString().Should().Contain("próxima ordem");
        reivindicar.GetProperty("responses").EnumerateObject().Select(r => r.Name).Should().Contain(["200", "204", "401", "409"]);
        var concluir = swagger.GetProperty("paths").GetProperty("/api/ordens-de-producao/{id}/diagnostico/concluir").GetProperty("post");
        concluir.GetProperty("responses").EnumerateObject().Select(r => r.Name).Should().Contain(["200", "400", "404", "409"]);
    }

    [Fact]
    public async Task Swagger_ServersDeveRefletirOHostDaRequisicao()
    {
        var swagger = await ObterSwaggerAsync();

        swagger.GetProperty("servers")[0].GetProperty("url").GetString().Should().Be("http://localhost");
    }

    [Fact]
    public async Task Swagger_AtrasDoGateway_DeveUsarSchemeHostEPrefixoEncaminhados()
    {
        var swagger = await ObterSwaggerAsync(requisicao =>
        {
            requisicao.Headers.Add("X-Forwarded-Proto", "https");
            requisicao.Headers.Add("X-Forwarded-Host", "apimfiap.azure-api.net");
            requisicao.Headers.Add("X-Forwarded-Prefix", "/execucaoserver");
        });

        swagger.GetProperty("servers")[0].GetProperty("url").GetString().Should().Be("https://apimfiap.azure-api.net/execucaoserver");
    }

    [Fact]
    public async Task SwaggerUi_DeveServirNaRaiz()
    {
        var resposta = await Api.CreateClient().GetAsync("/");

        resposta.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("swagger/v1/swagger.json");
    }

    [Fact]
    public async Task Health_DeveResponderSaudavelSemDependerDoMongo()
    {
        var resposta = await Api.CreateClient().GetAsync("/health");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task HealthReady_ComMongoInacessivel_DeveResponder503()
    {
        var resposta = await Api.CreateClient().GetAsync("/health/ready");

        resposta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var bruto = await resposta.Content.ReadAsStringAsync();
        var corpo = JsonDocument.Parse(bruto).RootElement;
        corpo.GetProperty("status").GetString().Should().Be("Unhealthy");
        corpo.GetProperty("checks").GetProperty("mongodb").GetProperty("status").GetString().Should().Be("Unhealthy");
        corpo.GetProperty("checks").GetProperty("mongodb").GetProperty("descricao").GetString().Should().Be("MongoDB indisponível.");
        corpo.GetProperty("checks").GetProperty("masstransit-bus").GetProperty("status").GetString().Should().Be("Healthy");

        // O MongoTimeoutException real traz o endpoint ("localhost:1"), o estado do cluster ("Disconnected",
        // "ClusterId") e o proprio nome do tipo; o corpo e anonimo e nunca pode expor nada disso.
        bruto.Should().NotContain("localhost:1")
            .And.NotContain("Disconnected")
            .And.NotContain("ClusterId")
            .And.NotContain("MongoTimeoutException")
            .And.NotContain("Exception");
    }

    // Falha do check do bus nao pode tirar o pod de rotacao: o MassTransit o registra com FailureStatus padrao
    // (Unhealthy, 503) e o servico o rebaixa para Degraded; sem isso uma excecao no check derrubaria a readiness.
    [Fact]
    public void CheckDoBus_DeveTerAFalhaRebaixadaParaDegradedEFazerParteDaProntidao()
    {
        var registros = Api.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        var bus = registros.Should().ContainSingle(r => r.Name == "masstransit-bus").Subject;

        bus.FailureStatus.Should().Be(HealthStatus.Degraded);
        bus.Tags.Should().Contain("ready");
    }

    [Fact]
    public async Task Metrics_DeveExporMetricasHttpNoFormatoPrometheus()
    {
        await Api.CreateClient().GetAsync("/health");

        var resposta = await Api.CreateClient().GetAsync("/metrics");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("http_request_duration_seconds");
    }

    [Fact]
    public void Servicos_DevemRegistrarOsPipelineBehaviorsNaOrdemValidacaoDepoisTransacao()
    {
        using var escopo = Api.Services.CreateScope();

        var comportamentos = escopo.ServiceProvider
            .GetServices<MediatR.IPipelineBehavior<Application.Commands.ConcluirDiagnostico.ConcluirDiagnosticoCommand, Application.DTOs.OrdemDeProducaoDto>>()
            .Select(b => b.GetType().Name.Split('`')[0])
            .ToList();

        comportamentos.Should().Equal("ValidationBehavior", "TransacaoBehavior");
    }
}
