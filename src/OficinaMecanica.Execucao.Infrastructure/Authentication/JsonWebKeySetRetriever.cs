using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace OficinaMecanica.Execucao.Infrastructure.Authentication;

// Retriever minimo para um JWKS cru (so o array de chaves, sem o documento de discovery OIDC): o
// Microsoft.IdentityModel.Protocols so traz um pronto para o OpenIdConnectConfiguration inteiro. Baixo o
// JSON com o IDocumentRetriever padrao e desserializo direto como JsonWebKeySet.
public sealed class JsonWebKeySetRetriever : IConfigurationRetriever<JsonWebKeySet>
{
    public async Task<JsonWebKeySet> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var documento = await retriever.GetDocumentAsync(address, cancel).ConfigureAwait(false);
        return new JsonWebKeySet(documento);
    }
}
