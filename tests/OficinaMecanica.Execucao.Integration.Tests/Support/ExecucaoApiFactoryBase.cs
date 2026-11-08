using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;

namespace OficinaMecanica.Execucao.Integration.Tests.Support;

// Sobe a API inteira (Program.cs) e troca so a resolucao da chave JWT pela chave RSA local, sem depender do
// JWKS do Seguranca. Issuer e audience continuam vindo da configuracao.
public abstract class ExecucaoApiFactoryBase : WebApplicationFactory<Program>
{
    public TokenDeTeste Tokens { get; } = new();

    protected abstract void ConfigurarAmbiente(IWebHostBuilder builder);

    // Falso so no teste que valida o caminho real: busca do JWKS por HTTP e resolucao da chave pelo kid.
    protected virtual bool UsaChaveLocal => true;

    protected sealed override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting e nao ConfigureAppConfiguration: o Program.cs le a configuracao enquanto registra os
        // servicos, antes de o host aplicar as fontes adicionadas depois.
        builder.UseSetting("JwtSettings:Issuer", TokenDeTeste.Issuer);
        builder.UseSetting("JwtSettings:Audience", TokenDeTeste.Audience);
        builder.UseSetting("JwtSettings:JwksUri", "http://localhost:1/jwks.json");

        // Sem cache de prontidao: os testes derrubam e restauram dependencias e esperam ver cada mudanca na hora. O cache
        // tem testes proprios (HealthCheckComCacheTests).
        builder.UseSetting("Health:ProntidaoCacheSegundos", "0");

        ConfigurarAmbiente(builder);

        if (!UsaChaveLocal)
            return;

        builder.ConfigureServices(services =>
        {
            var chave = Tokens.ChavePublica;
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opcoes =>
                opcoes.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, _, _) => new[] { chave });
        });
    }

    public HttpClient CriarClienteAutenticado(string? sub = "mecanico-01")
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Tokens.Gerar(sub));
        return cliente;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Tokens.Dispose();

        base.Dispose(disposing);
    }
}
