using MongoDB.Bson;
using MongoDB.Driver;
using OficinaMecanica.Execucao.Infrastructure.Observability;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Infra;

[Trait("Category", "Infra")]
[Collection(Colecoes.Infra)]
public class OutboxPendenteReaderTests(InfraFixture infra)
{
    // Banco proprio: as mensagens de mentira nao podem cair no servico de entrega de uma API de teste.
    private IMongoDatabase NovoBanco() =>
        new MongoClient(infra.StringDeConexaoDoMongo).GetDatabase("outbox_reader_" + Guid.NewGuid().ToString("N")[..8]);

    [Fact(Timeout = 120_000)]
    public async Task SemColecao_DeveContarZero()
    {
        var leitor = new MongoOutboxPendenteReader(NovoBanco());

        (await leitor.ContarAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact(Timeout = 120_000)]
    public async Task ComMensagensNoOutbox_DeveContarOBacklogSemFiltro()
    {
        var banco = NovoBanco();
        var colecao = banco.GetCollection<BsonDocument>("outbox.messages");
        await colecao.InsertManyAsync(Enumerable.Range(0, 4).Select(i => new BsonDocument("Corpo", i)));
        var leitor = new MongoOutboxPendenteReader(banco);

        (await leitor.ContarAsync(CancellationToken.None)).Should().Be(4);

        await colecao.DeleteOneAsync(new BsonDocument());
        (await leitor.ContarAsync(CancellationToken.None)).Should().Be(3, "a entrega remove a mensagem do outbox");
    }
}
