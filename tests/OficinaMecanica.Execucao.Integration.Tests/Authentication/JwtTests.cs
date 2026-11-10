using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using OficinaMecanica.Execucao.Infrastructure.Authentication;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Authentication;

public class ResolvedorDeChavesJwksTests
{
    // Gerenciador falso: serve um conjunto de chaves que o teste pode trocar, e conta os pedidos de refresh.
    private sealed class GerenciadorFalso : IConfigurationManager<JsonWebKeySet>
    {
        public JsonWebKeySet Atual { get; set; } = new();
        public JsonWebKeySet? AposRefresh { get; set; }
        public int Refreshes { get; private set; }

        public Task<JsonWebKeySet> GetConfigurationAsync(CancellationToken cancel) => Task.FromResult(Atual);

        public void RequestRefresh()
        {
            Refreshes++;
            if (AposRefresh is not null)
                Atual = AposRefresh;
        }
    }

    private static JsonWebKeySet Jwks(params string[] kids) =>
        new(JsonSerializer.Serialize(new
        {
            keys = kids.Select(kid => new { kty = "RSA", use = "sig", kid, alg = "RS256", n = "sXchDaQebHnPiGvyDOAT4saGEUetSyo9MKLOoWFsueri23bOdgWp4Dy1WlUzewbgBHod5pcM9H95GQRV3JDXboIRROSBigeC5yjU1hGzHHyXss8UDprecbAYxknTcQkhslANGRUZmdTOQ5qTRsLAt6BTYuyvVRdhS8exSZEy_c4gs_7svlJJQ4H9_NxsiIoLwAEk7-Q3UXERGYw_75IDrGA84-lA_-Ct4eTlXHBIY2EaV7t7LjJaynVJCpkv4LKjTTAumiGUIuQhrNhZLuF_RJLqHpM2kgWFLU7-VTdL1VbC2tejvcI2BlMkEpk1BzBZI0KQB0GaDWFLN-aEAw3vRw", e = "AQAB" })
        }));

    [Fact]
    public void KidConhecido_DeveDevolverAChaveSemPedirRefresh()
    {
        var gerenciador = new GerenciadorFalso { Atual = Jwks("kid-a", "kid-b") };

        var chaves = new ResolvedorDeChavesJwks(gerenciador).Resolver("kid-b").ToList();

        chaves.Should().ContainSingle().Which.KeyId.Should().Be("kid-b");
        gerenciador.Refreshes.Should().Be(0);
    }

    [Fact]
    public void KidDesconhecido_DevePedirRefreshEDevolverVazioSeContinuarAusente()
    {
        var gerenciador = new GerenciadorFalso { Atual = Jwks("kid-a") };

        var chaves = new ResolvedorDeChavesJwks(gerenciador).Resolver("kid-novo");

        chaves.Should().BeEmpty();
        gerenciador.Refreshes.Should().Be(1);
    }

    [Fact]
    public void KidDeChaveRotacionada_DeveEncontrarDepoisDoRefresh()
    {
        var gerenciador = new GerenciadorFalso { Atual = Jwks("kid-antigo"), AposRefresh = Jwks("kid-antigo", "kid-rotacionado") };

        var chaves = new ResolvedorDeChavesJwks(gerenciador).Resolver("kid-rotacionado").ToList();

        chaves.Should().ContainSingle().Which.KeyId.Should().Be("kid-rotacionado");
        gerenciador.Refreshes.Should().Be(1);
    }

    [Fact]
    public void KidNulo_NaoDeveCasarComNenhumaChave()
    {
        new ResolvedorDeChavesJwks(new GerenciadorFalso { Atual = Jwks("kid-a") }).Resolver(null).Should().BeEmpty();
    }
}

public class JsonWebKeySetRetrieverTests
{
    private sealed class DocumentoFixo(string json) : IDocumentRetriever
    {
        public string? Endereco { get; private set; }

        public Task<string> GetDocumentAsync(string address, CancellationToken cancel)
        {
            Endereco = address;
            return Task.FromResult(json);
        }
    }

    [Fact]
    public async Task GetConfigurationAsync_DeveDesserializarOJwksCruDoDocumento()
    {
        using var tokens = new TokenDeTeste();
        var documento = new DocumentoFixo(JsonSerializer.Serialize(new { keys = new[] { tokens.ChaveJwks() } }));

        var jwks = await new JsonWebKeySetRetriever().GetConfigurationAsync("http://seguranca/.well-known/jwks.json", documento, CancellationToken.None);

        jwks.Keys.Should().ContainSingle().Which.Kid.Should().Be(TokenDeTeste.Kid);
        documento.Endereco.Should().Be("http://seguranca/.well-known/jwks.json");
    }
}

public class AutenticacaoJwtExtensionsTests
{
    [Theory]
    [InlineData("https://apimfiap.azure-api.net/segurancaserver/.well-known/jwks.json", true)]
    [InlineData("HTTPS://apim.exemplo/jwks.json", true)]
    [InlineData("http://host.docker.internal:7071/.well-known/jwks.json", false)]
    public void CriarRecuperadorDeDocumentos_SoExigeHttpsQuandoAUriEHttps(string uri, bool exigeHttps)
    {
        AutenticacaoJwtExtensions.CriarRecuperadorDeDocumentos(uri).RequireHttps.Should().Be(exigeHttps);
    }

    [Fact]
    public void AddAutenticacaoJwt_SemJwksUri_DeveFalharNaConfiguracao()
    {
        var acao = () => new ServiceCollection().AddAutenticacaoJwt(new ConfigurationBuilder().Build());

        acao.Should().Throw<InvalidOperationException>().WithMessage("*JwksUri*");
    }

    [Fact]
    public async Task AddAutenticacaoJwt_DeveConfigurarEmissorAudienciaEClaimsDoSeguranca()
    {
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Issuer"] = "OficinaMecanica.Seguranca",
            ["JwtSettings:Audience"] = "OficinaMecanica.Client",
            ["JwtSettings:JwksUri"] = "http://localhost:7071/.well-known/jwks.json"
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddAutenticacaoJwt(configuracao);
        using var provider = services.BuildServiceProvider();

        var opcoes = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        var esquema = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetDefaultAuthenticateSchemeAsync();

        esquema!.Name.Should().Be(JwtBearerDefaults.AuthenticationScheme);
        opcoes.MapInboundClaims.Should().BeFalse();
        opcoes.TokenValidationParameters.ValidIssuer.Should().Be("OficinaMecanica.Seguranca");
        opcoes.TokenValidationParameters.ValidAudience.Should().Be("OficinaMecanica.Client");
        opcoes.TokenValidationParameters.ValidateLifetime.Should().BeTrue();
        opcoes.TokenValidationParameters.ValidateIssuerSigningKey.Should().BeTrue();
        opcoes.TokenValidationParameters.ClockSkew.Should().Be(TimeSpan.Zero);
        opcoes.TokenValidationParameters.NameClaimType.Should().Be("sub");
        opcoes.TokenValidationParameters.RoleClaimType.Should().Be("role");
    }
}

// Caminho real, sem trocar a resolucao da chave: um servidor HTTP local faz o papel do Seguranca e serve o JWKS.
public class ValidacaoPorJwksRealTests : IAsyncLifetime
{
    private WebApplication _servidorDeJwks = null!;
    private readonly TokenDeTeste _emissor = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _servidorDeJwks = builder.Build();
        var jwks = JsonSerializer.Serialize(new { keys = new[] { _emissor.ChaveJwks() } });
        _servidorDeJwks.MapGet("/.well-known/jwks.json", () => Results.Text(jwks, "application/json"));
        await _servidorDeJwks.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _servidorDeJwks.DisposeAsync();
        _emissor.Dispose();
    }

    private string UriDoJwks => _servidorDeJwks.Urls.First() + "/.well-known/jwks.json";

    [Fact]
    public async Task TokenAssinadoPelaChaveDoJwks_DeveSerAceitoViaHttp()
    {
        using var api = new ApiSemInfraFactory(jwksUriReal: UriDoJwks);
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _emissor.Gerar());

        var resposta = await cliente.GetAsync("/api/fila/resumo");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TokenAssinadoPorChaveForaDoJwks_DeveSerRecusado()
    {
        using var api = new ApiSemInfraFactory(jwksUriReal: UriDoJwks);
        using var outroEmissor = new TokenDeTeste();
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", outroEmissor.Gerar());

        var resposta = await cliente.GetAsync("/api/fila/resumo");

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task JwksIndisponivel_DeveRecusarOTokenSemDerrubarAApi()
    {
        using var api = new ApiSemInfraFactory(jwksUriReal: "http://127.0.0.1:1/.well-known/jwks.json");
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _emissor.Gerar());

        var resposta = await cliente.GetAsync("/api/fila/resumo");

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await api.CreateClient().GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
