using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace OficinaMecanica.Execucao.Integration.Tests.Support;

// Par RSA local e emissor de JWT RS256 de teste, no mesmo formato do Seguranca (claims sub e role).
public sealed class TokenDeTeste : IDisposable
{
    public const string Issuer = "OficinaMecanica.Seguranca";
    public const string Audience = "OficinaMecanica.Client";
    public const string Kid = "kid-de-teste";

    private readonly RSA _rsa = RSA.Create(2048);

    public RsaSecurityKey ChavePublica => new(_rsa.ExportParameters(false)) { KeyId = Kid };

    public JsonWebKey ChaveJwks()
    {
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(ChavePublica);
        jwk.Kid = Kid;
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;
        return jwk;
    }

    public string Gerar(string? sub = "mecanico-01", string role = "Admin", DateTime? expira = null, string issuer = Issuer, string audience = Audience)
    {
        var credenciais = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = Kid }, SecurityAlgorithms.RsaSha256);

        var claims = new List<System.Security.Claims.Claim> { new("role", role) };
        if (sub is not null)
            claims.Add(new System.Security.Claims.Claim("sub", sub));

        var token = new JwtSecurityToken(issuer, audience, claims, expires: expira ?? DateTime.UtcNow.AddMinutes(30), signingCredentials: credenciais);
        return new JwtSecurityTokenHandler { SetDefaultTimesOnTokenCreation = false }.WriteToken(token);
    }

    public void Dispose() => _rsa.Dispose();
}
