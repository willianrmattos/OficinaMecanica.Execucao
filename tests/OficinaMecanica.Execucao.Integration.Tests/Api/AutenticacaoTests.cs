using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OficinaMecanica.Execucao.API.Middleware;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Api;

public class AutenticacaoTests : ApiTestBase
{
    private static readonly Guid Id = Guid.NewGuid();

    public static IEnumerable<object[]> RotasProtegidas() =>
    [
        [HttpMethod.Post, "/api/diagnosticos/proximo"],
        [HttpMethod.Post, "/api/reparos/proximo"],
        [HttpMethod.Post, $"/api/ordens-de-producao/{Id}/apontamentos"],
        [HttpMethod.Post, $"/api/ordens-de-producao/{Id}/diagnostico/concluir"],
        [HttpMethod.Post, $"/api/ordens-de-producao/{Id}/reparo/finalizar"],
        [HttpMethod.Post, $"/api/ordens-de-producao/{Id}/reparo/inviabilizar"],
        [HttpMethod.Get, $"/api/ordens-de-producao/{Id}"],
        [HttpMethod.Get, "/api/fila"],
        [HttpMethod.Get, "/api/fila/resumo"]
    ];

    [Theory]
    [MemberData(nameof(RotasProtegidas))]
    public async Task RotaProtegida_SemToken_DeveResponder401(HttpMethod metodo, string rota)
    {
        var resposta = await Api.CreateClient().SendAsync(new HttpRequestMessage(metodo, rota));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpStatusCode> ConsultarComAsync(string token)
    {
        var cliente = Api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await cliente.GetAsync("/api/fila/resumo")).StatusCode;
    }

    [Fact]
    public async Task TokenValido_DeveSerAceito()
    {
        (await ConsultarComAsync(Api.Tokens.Gerar())).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TokenExpirado_DeveResponder401()
    {
        (await ConsultarComAsync(Api.Tokens.Gerar(expira: DateTime.UtcNow.AddMinutes(-5)))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenComAudienceErrada_DeveResponder401()
    {
        (await ConsultarComAsync(Api.Tokens.Gerar(audience: "outra-audience"))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenComIssuerErrado_DeveResponder401()
    {
        (await ConsultarComAsync(Api.Tokens.Gerar(issuer: "emissor-desconhecido"))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenAssinadoPorOutraChave_DeveResponder401()
    {
        using var outraChave = new TokenDeTeste();

        (await ConsultarComAsync(outraChave.Gerar())).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenSemClaimSub_DeveResponder401ComMensagemAoReivindicar()
    {
        var cliente = Api.CriarClienteAutenticado(sub: null);

        var resposta = await cliente.PostAsync("/api/diagnosticos/proximo", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var erro = await resposta.Content.ReadFromJsonAsync<ErrorResponse>();
        erro!.Mensagem.Should().Contain("sub");
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/metrics")]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/")]
    public async Task EndpointsOperacionais_DevemSerAnonimos(string rota)
    {
        var resposta = await Api.CreateClient().GetAsync(rota);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
