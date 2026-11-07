using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;

namespace OficinaMecanica.Execucao.Infrastructure.Persistence;

public sealed record MongoInitializerOptions
{
    // Pre-cria colecoes e indice do inbox/outbox do MassTransit (so faz sentido com o outbox do Mongo ligado).
    public bool PreCriarColecoesDoOutbox { get; init; } = true;
}

// Registrado ANTES do MassTransit: os hosted services iniciam na ordem de registro e as colecoes e indices
// precisam existir antes do bus consumir a primeira mensagem. Todas as operacoes sao idempotentes.
public sealed class MongoInitializer(IMongoDatabase banco, MongoInitializerOptions opcoes) : IHostedService
{
    public static readonly string[] ColecoesDoMassTransit = ["inbox.states", "outbox.messages", "outbox.states"];

    private const int CodigoNamespaceExists = 48;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await CriarIndicesDaFilaAsync(banco, cancellationToken);

        if (opcoes.PreCriarColecoesDoOutbox)
            await PreCriarColecoesDoMassTransitAsync(banco, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task CriarIndicesDaFilaAsync(IMongoDatabase banco, CancellationToken cancellationToken = default)
    {
        var colecao = banco.GetCollection<BsonDocument>(MongoMappings.ColecaoDeOrdens);
        var chaves = Builders<BsonDocument>.IndexKeys;

        await colecao.Indexes.CreateManyAsync(
        [
            // Fila de diagnostico (FIFO por EnfileiradaEm) e de reparo (FIFO por LiberadaEm).
            new CreateIndexModel<BsonDocument>(
                chaves.Ascending("status").Ascending("enfileiradaEm"),
                new CreateIndexOptions { Name = "ix_status_enfileiradaEm" }),
            new CreateIndexModel<BsonDocument>(
                chaves.Ascending("status").Ascending("liberadaEm"),
                new CreateIndexOptions { Name = "ix_status_liberadaEm" }),
            new CreateIndexModel<BsonDocument>(
                chaves.Ascending("mecanicoId").Ascending("status"),
                new CreateIndexOptions { Name = "ix_mecanicoId_status" }),
            // Parcial: o tombstone nao tem numero e nao entra no indice.
            new CreateIndexModel<BsonDocument>(
                chaves.Ascending("numero"),
                new CreateIndexOptions<BsonDocument>
                {
                    Name = "ux_numero",
                    Unique = true,
                    PartialFilterExpression = new BsonDocument(MongoMappings.CampoDiscriminador, MongoMappings.TipoOrdem)
                })
        ], cancellationToken);
    }

    // Sem pre-criar, a primeira mensagem pode colidir com o DDL (criacao de colecao/indice) que o
    // InboxCleanupService do MassTransit faz em background: dentro de transacao o Mongo responde com
    // erro de lock/WriteConflict e a mensagem iria para a fila de erro sem motivo de negocio.
    public static async Task PreCriarColecoesDoMassTransitAsync(IMongoDatabase banco, CancellationToken cancellationToken = default)
    {
        foreach (var nome in ColecoesDoMassTransit)
        {
            try
            {
                await banco.CreateCollectionAsync(nome, cancellationToken: cancellationToken);
            }
            catch (MongoCommandException ex) when (ex.Code == CodigoNamespaceExists)
            {
                // Ja existe: o start e idempotente.
            }
        }

        // Mesma especificacao que o MassTransit usa para deduplicar (nome padrao MessageId_1_ConsumerId_1, unico).
        // Optei por criar o indice aqui, antes do bus, porque o InboxCleanupService o criaria em background e a
        // criacao (DDL) colidiria com a primeira mensagem consumida dentro de transacao. Nenhum teste distingue esta
        // criacao da que o proprio MassTransit faria depois (o indice resultante e identico); a protecao e a
        // ordem de partida e o custo e so manter a especificacao igual a dele.
        await banco.GetCollection<BsonDocument>("inbox.states").Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("MessageId").Ascending("ConsumerId"),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: cancellationToken);
    }
}
