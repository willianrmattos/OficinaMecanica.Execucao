using MongoDB.Bson;
using MongoDB.Driver;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Infrastructure.Persistence;

namespace OficinaMecanica.Execucao.Infrastructure.Observability;

// Fonte do alerta de SLA: so metrica, nunca cancela nem muda ordem. Devolve, por status, desde quando a
// ordem mais antiga esta nele; quem consome calcula a idade na hora de observar.
public interface IIdadeDaFilaReader
{
    Task<IReadOnlyDictionary<StatusProducao, DateTimeOffset>> ObterInicioDaMaisAntigaAsync(CancellationToken cancellationToken);
}

public sealed class MongoIdadeDaFilaReader(IMongoDatabase banco) : IIdadeDaFilaReader
{
    private readonly IMongoCollection<BsonDocument> _documentos = banco.GetCollection<BsonDocument>(MongoMappings.ColecaoDeOrdens);

    public async Task<IReadOnlyDictionary<StatusProducao, DateTimeOffset>> ObterInicioDaMaisAntigaAsync(CancellationToken cancellationToken)
    {
        var resultado = new Dictionary<StatusProducao, DateTimeOffset>();

        // Na fila de diagnostico conta desde que entrou; na de reparo, desde que foi liberada (o mesmo
        // criterio que ordena o claim).
        await AdicionarAsync(resultado, StatusProducao.Fila, "enfileiradaEm", cancellationToken);
        await AdicionarAsync(resultado, StatusProducao.AguardandoExecucao, "liberadaEm", cancellationToken);

        // Em andamento, o relogio do SLA e o inicio da etapa corrente (diagnostico ou reparo).
        await AdicionarDaEtapaAsync(resultado, StatusProducao.EmDiagnostico, NomeEtapa.Diagnostico, cancellationToken);
        await AdicionarDaEtapaAsync(resultado, StatusProducao.EmExecucao, NomeEtapa.Reparo, cancellationToken);

        return resultado;
    }

    private async Task AdicionarAsync(
        Dictionary<StatusProducao, DateTimeOffset> resultado, StatusProducao status, string campo, CancellationToken cancellationToken)
    {
        var maisAntiga = await _documentos
            .Find(FiltroDoStatus(status))
            .Sort(Builders<BsonDocument>.Sort.Ascending(campo))
            .Project(Builders<BsonDocument>.Projection.Include(campo).Exclude("_id"))
            .Limit(1)
            .FirstOrDefaultAsync(cancellationToken);

        if (maisAntiga is not null && maisAntiga.TryGetValue(campo, out var valor) && valor.IsValidDateTime)
            resultado[status] = ParaDataUtc(valor);
    }

    private async Task AdicionarDaEtapaAsync(
        Dictionary<StatusProducao, DateTimeOffset> resultado, StatusProducao status, NomeEtapa etapa, CancellationToken cancellationToken)
    {
        var grupos = await _documentos.Aggregate()
            .Match(FiltroDoStatus(status))
            .Unwind("etapas")
            .Match(new BsonDocument
            {
                { "etapas.nome", etapa.ToString() },
                { "etapas.status", StatusEtapa.EmAndamento.ToString() }
            })
            .Group(new BsonDocument { { "_id", BsonNull.Value }, { "desde", new BsonDocument("$min", "$etapas.inicio") } })
            .ToListAsync(cancellationToken);

        if (grupos.Count > 0 && grupos[0].TryGetValue("desde", out var valor) && valor.IsValidDateTime)
            resultado[status] = ParaDataUtc(valor);
    }

    private static BsonDocument FiltroDoStatus(StatusProducao status) =>
        new()
        {
            { MongoMappings.CampoDiscriminador, MongoMappings.TipoOrdem },
            { "status", status.ToString() }
        };

    private static DateTimeOffset ParaDataUtc(BsonValue valor) =>
        new(valor.ToUniversalTime(), TimeSpan.Zero);
}
