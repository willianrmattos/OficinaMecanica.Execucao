using MongoDB.Bson;
using MongoDB.Driver;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Infrastructure.Observability;

namespace OficinaMecanica.Execucao.Infrastructure.Persistence;

public sealed class OrdemDeProducaoRepository : IOrdemDeProducaoRepository
{
    private static readonly FilterDefinitionBuilder<OrdemDeProducao> Filtro = Builders<OrdemDeProducao>.Filter;
    private static readonly SortDefinitionBuilder<OrdemDeProducao> Ordem = Builders<OrdemDeProducao>.Sort;

    private readonly IMongoCollection<OrdemDeProducao> _ordens;
    private readonly IMongoCollection<BsonDocument> _documentos;
    private readonly IMongoSessionAccessor _sessoes;
    private readonly IMetricasPendentes _metricas;

    public OrdemDeProducaoRepository(IMongoDatabase banco, IMongoSessionAccessor sessoes, IMetricasPendentes metricas)
    {
        _ordens = banco.GetCollection<OrdemDeProducao>(MongoMappings.ColecaoDeOrdens);
        _documentos = banco.GetCollection<BsonDocument>(MongoMappings.ColecaoDeOrdens);
        _sessoes = sessoes;
        _metricas = metricas;
    }

    public async Task<OrdemDeProducao?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var filtro = Filtro.Eq(x => x.Id, id) & SomenteOrdens;
        var sessao = _sessoes.Current;

        var consulta = sessao is null ? _ordens.Find(filtro) : _ordens.Find(sessao, filtro);
        return await consulta.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> AdicionarAsync(OrdemDeProducao ordem, CancellationToken cancellationToken = default)
    {
        var sessao = _sessoes.Current;

        try
        {
            if (sessao is null)
                await _ordens.InsertOneAsync(ordem, cancellationToken: cancellationToken);
            else
                await _ordens.InsertOneAsync(sessao, ordem, cancellationToken: cancellationToken);
        }
        // Fora de transacao a colisao no _id vira "ja existe" (reentrega, ou tombstone de cancelamento que
        // chegou primeiro: nos dois casos nada deve ser enfileirado). Dentro de transacao o E11000 aborta
        // tudo e a excecao sobe: o retry reexecuta o handler inteiro e a pre-checagem resolve.
        catch (MongoWriteException ex) when (sessao is null && ex.EhColisaoDoId())
        {
            return false;
        }

        _metricas.OrdemEnfileirada();
        return true;
    }

    public async Task AtualizarAsync(OrdemDeProducao ordem, CancellationToken cancellationToken = default)
    {
        var versaoEsperada = ordem.Version;

        // Gravo o documento ja com a proxima Version, mas so incremento a ordem em memoria depois de a
        // escrita ser aceita: se o filtro nao casar, a ordem lida continua refletindo o que foi persistido.
        var documento = ordem.ToBsonDocument();
        documento["version"] = versaoEsperada + 1;

        var filtro = new BsonDocument
        {
            { "_id", new BsonBinaryData(ordem.Id, GuidRepresentation.Standard) },
            { MongoMappings.CampoDiscriminador, MongoMappings.TipoOrdem },
            { "version", versaoEsperada }
        };

        var sessao = _sessoes.Current;
        var resultado = sessao is null
            ? await _documentos.ReplaceOneAsync(filtro, documento, cancellationToken: cancellationToken)
            : await _documentos.ReplaceOneAsync(sessao, filtro, documento, cancellationToken: cancellationToken);

        if (resultado.MatchedCount == 0)
            throw new ConcorrenciaException(
                $"A ordem de produção {ordem.Id} foi alterada por outra operação (versão esperada: {versaoEsperada}).");

        ordem.IncrementVersion();
        _metricas.OrdemGravada(ordem.Status);
    }

    // Leio a fila SEM a sessao da transacao: assim cada releitura enxerga o estado commitado (uma
    // ConcorrenciaException devolve a proxima ordem, nao a mesma disputada) e o snapshot da transacao
    // so nasce na escrita do claim, que fica na mesma transacao do outbox.
    public async Task<OrdemDeProducao?> ObterProximaDaFilaAsync(StatusProducao status, CancellationToken cancellationToken = default)
    {
        var filtro = Filtro.Eq(x => x.Status, status) & SomenteOrdens;

        return await _ordens.Find(filtro)
            .Sort(OrdenacaoDaFila(status))
            .Limit(1)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrdemDeProducao>> ListarAsync(
        StatusProducao? status, string? mecanicoId, int pagina, int tamanho, CancellationToken cancellationToken = default)
    {
        return await _ordens.Find(FiltroDeListagem(status, mecanicoId))
            .Sort(status.HasValue ? OrdenacaoDaFila(status.Value) : Ordem.Ascending(x => x.EnfileiradaEm))
            .Skip((pagina - 1) * tamanho)
            .Limit(tamanho)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> ContarAsync(StatusProducao? status, string? mecanicoId, CancellationToken cancellationToken = default)
    {
        var total = await _ordens.CountDocumentsAsync(FiltroDeListagem(status, mecanicoId), cancellationToken: cancellationToken);
        return (int)total;
    }

    public async Task<IReadOnlyDictionary<StatusProducao, int>> ContarPorStatusAsync(CancellationToken cancellationToken = default)
    {
        var grupos = await _ordens.Aggregate()
            .Match(SomenteOrdens)
            .Group(x => x.Status, g => new { Status = g.Key, Total = g.Count() })
            .ToListAsync(cancellationToken);

        return grupos.ToDictionary(g => g.Status, g => g.Total);
    }

    private static FilterDefinition<OrdemDeProducao> SomenteOrdens =>
        Filtro.Eq(MongoMappings.CampoDiscriminador, MongoMappings.TipoOrdem);

    private static FilterDefinition<OrdemDeProducao> FiltroDeListagem(StatusProducao? status, string? mecanicoId)
    {
        var filtro = SomenteOrdens;

        if (status.HasValue)
            filtro &= Filtro.Eq(x => x.Status, status.Value);

        if (!string.IsNullOrWhiteSpace(mecanicoId))
            filtro &= Filtro.Eq(x => x.MecanicoId, mecanicoId);

        return filtro;
    }

    // A vez na fila de reparo e decidida por quando a ordem ficou liberada, nao por quando chegou.
    private static SortDefinition<OrdemDeProducao> OrdenacaoDaFila(StatusProducao status) =>
        status == StatusProducao.AguardandoExecucao
            ? Ordem.Ascending(x => x.LiberadaEm)
            : Ordem.Ascending(x => x.EnfileiradaEm);
}
