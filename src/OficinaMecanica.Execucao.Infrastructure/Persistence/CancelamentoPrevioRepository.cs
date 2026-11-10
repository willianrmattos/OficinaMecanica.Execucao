using MongoDB.Bson;
using MongoDB.Driver;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Infrastructure.Observability;

namespace OficinaMecanica.Execucao.Infrastructure.Persistence;

// O tombstone vive na mesma colecao e com o mesmo _id da ordem (ver MongoMappings): ordem e tombstone
// nunca coexistem para um OrdemDeServicoId, e a corrida entre Enfileirar e CancelarProducao termina numa
// colisao de chave unica em vez de deixar uma ordem orfa na Fila.
public sealed class CancelamentoPrevioRepository : ICancelamentoPrevioRepository
{
    private readonly IMongoCollection<CancelamentoPrevio> _tombstones;
    private readonly IMongoCollection<BsonDocument> _documentos;
    private readonly IMongoSessionAccessor _sessoes;
    private readonly IMetricasPendentes _metricas;

    public CancelamentoPrevioRepository(IMongoDatabase banco, IMongoSessionAccessor sessoes, IMetricasPendentes metricas)
    {
        _tombstones = banco.GetCollection<CancelamentoPrevio>(MongoMappings.ColecaoDeOrdens);
        _documentos = banco.GetCollection<BsonDocument>(MongoMappings.ColecaoDeOrdens);
        _sessoes = sessoes;
        _metricas = metricas;
    }

    public async Task<CancelamentoPrevio?> ObterAsync(Guid ordemDeServicoId, CancellationToken cancellationToken = default)
    {
        var filtro = Builders<CancelamentoPrevio>.Filter.Eq(x => x.Id, ordemDeServicoId)
                     & Builders<CancelamentoPrevio>.Filter.Eq(MongoMappings.CampoDiscriminador, MongoMappings.TipoCancelamentoPrevio);
        var sessao = _sessoes.Current;

        var consulta = sessao is null ? _tombstones.Find(filtro) : _tombstones.Find(sessao, filtro);
        return await consulta.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> AdicionarAsync(CancelamentoPrevio cancelamento, CancellationToken cancellationToken = default)
    {
        var sessao = _sessoes.Current;

        try
        {
            if (sessao is null)
                await _tombstones.InsertOneAsync(cancelamento, cancellationToken: cancellationToken);
            else
                await _tombstones.InsertOneAsync(sessao, cancelamento, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (sessao is null && ex.EhColisaoDoId())
        {
            // So devolvo false quando quem ocupa o _id e outro tombstone (reentrega). Se for a ordem, o
            // cancelamento nao pode ser descartado: o retry reexecuta o handler, que agora acha a ordem e a cancela.
            if (await JaExisteTombstoneAsync(cancelamento.OrdemDeServicoId, cancellationToken))
                return false;

            throw new ConcorrenciaException(
                $"A ordem de produção {cancelamento.OrdemDeServicoId} foi enfileirada em paralelo ao cancelamento prévio.");
        }

        _metricas.CancelamentoPrevioRegistrado();
        return true;
    }

    private async Task<bool> JaExisteTombstoneAsync(Guid ordemDeServicoId, CancellationToken cancellationToken)
    {
        var filtro = new BsonDocument
        {
            { "_id", new BsonBinaryData(ordemDeServicoId, GuidRepresentation.Standard) },
            { MongoMappings.CampoDiscriminador, MongoMappings.TipoCancelamentoPrevio }
        };

        return await _documentos.CountDocumentsAsync(filtro, new CountOptions { Limit = 1 }, cancellationToken) > 0;
    }
}
