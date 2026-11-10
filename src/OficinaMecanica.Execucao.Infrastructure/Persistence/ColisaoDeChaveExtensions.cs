using MongoDB.Driver;

namespace OficinaMecanica.Execucao.Infrastructure.Persistence;

internal static class ColisaoDeChaveExtensions
{
    // O E11000 do indice unico do numero tambem e DuplicateKey, mas nao significa "ja existe": so a colisao
    // no _id e idempotencia. O nome do indice vem na mensagem do servidor ("index: _id_ dup key: ...").
    public static bool EhColisaoDoId(this MongoWriteException ex) =>
        ex.WriteError.Category == ServerErrorCategory.DuplicateKey
        && ex.WriteError.Message.Contains("index: _id_", StringComparison.Ordinal);
}
