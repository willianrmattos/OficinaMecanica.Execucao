using MongoDB.Bson;
using MongoDB.Driver;

namespace OficinaMecanica.Execucao.Infrastructure.Observability;

// Mensagens que o outbox ainda nao entregou ao broker: o servico de entrega remove cada uma depois de publicar, entao
// um numero que so cresce e o sinal de broker fora (ou de entrega travada). E o alerta operacional que substitui a
// readiness do broker, que de proposito nao tira o pod de rotacao.
public interface IOutboxPendenteReader
{
    Task<long> ContarAsync(CancellationToken cancellationToken);
}

public sealed class MongoOutboxPendenteReader(IMongoDatabase banco) : IOutboxPendenteReader
{
    // Optei pela contagem estimada (metadados da colecao): ela roda a cada ciclo de metricas, justamente quando o
    // backlog cresce, e um CountDocuments sem filtro varreria a colecao inteira a cada vez. Para um alerta de
    // tendencia a estimativa basta.
    public async Task<long> ContarAsync(CancellationToken cancellationToken) =>
        await banco.GetCollection<BsonDocument>("outbox.messages")
            .EstimatedDocumentCountAsync(cancellationToken: cancellationToken);
}
