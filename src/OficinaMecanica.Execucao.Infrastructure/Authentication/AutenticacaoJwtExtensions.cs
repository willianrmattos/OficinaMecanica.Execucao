using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace OficinaMecanica.Execucao.Infrastructure.Authentication;

public static class AutenticacaoJwtExtensions
{
    // So valida tokens RS256 emitidos pelo OficinaMecanica.Seguranca: a chave publica vem do JWKS por
    // HTTP, sem segredo compartilhado. Este servico nao emite token.
    public static IServiceCollection AddAutenticacaoJwt(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection("JwtSettings");
        var jwksUri = jwt["JwksUri"] ?? throw new InvalidOperationException("JwtSettings:JwksUri não configurado.");

        var gerenciador = new ConfigurationManager<JsonWebKeySet>(jwksUri, new JsonWebKeySetRetriever(), CriarRecuperadorDeDocumentos(jwksUri));
        var resolvedor = new ResolvedorDeChavesJwks(gerenciador);

        services.AddAuthentication(opcoes =>
            {
                opcoes.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                opcoes.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(opcoes =>
            {
                // O id do mecanico e o claim "sub" como o Seguranca o emite; sem o mapeamento de nomes
                // legado o claim chega com o nome original.
                opcoes.MapInboundClaims = false;
                opcoes.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt["Issuer"],
                    ValidAudience = jwt["Audience"],
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                    IssuerSigningKeyResolver = (_, _, kid, _) => resolvedor.Resolver(kid),
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.AddAuthorization();

        return services;
    }

    // O HttpDocumentRetriever recusa http:// por padrao. Em producao o JWKS sempre passa pela APIM (https); em
    // desenvolvimento local o Seguranca roda sem TLS, entao so nesse caso libero.
    public static HttpDocumentRetriever CriarRecuperadorDeDocumentos(string jwksUri) =>
        new() { RequireHttps = jwksUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) };
}
