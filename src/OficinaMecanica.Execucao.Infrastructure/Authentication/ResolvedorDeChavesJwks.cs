using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace OficinaMecanica.Execucao.Infrastructure.Authentication;

// Resolve a chave de assinatura pelo kid do token a partir do JWKS em cache. Quando o kid nao esta no cache
// (o Seguranca rotacionou a chave ou foi reiniciado com outra), peco um refresh ao gerenciador, em vez de
// esperar o intervalo automatico, e tento mais uma vez.
public sealed class ResolvedorDeChavesJwks(IConfigurationManager<JsonWebKeySet> gerenciador)
{
    public IEnumerable<SecurityKey> Resolver(string? kid)
    {
        var chaves = ChavesComKid(kid);
        if (chaves.Count > 0)
            return chaves;

        gerenciador.RequestRefresh();
        return ChavesComKid(kid);
    }

    private List<SecurityKey> ChavesComKid(string? kid) =>
        gerenciador.GetConfigurationAsync(CancellationToken.None).GetAwaiter().GetResult()
            .Keys
            .Where(chave => chave.Kid == kid)
            .Cast<SecurityKey>()
            .ToList();
}
