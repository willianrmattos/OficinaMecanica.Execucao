using MassTransit.MongoDbIntegration;
using MongoDB.Driver;

namespace OficinaMecanica.Execucao.Infrastructure.Persistence;

// Ponte entre o escopo de DI e a sessao Mongo: so a Infrastructure conhece o MongoDbContext do
// MassTransit. Os repositorios leem a sessao por aqui e usam a transacao corrente quando existir.
public interface IMongoSessionAccessor
{
    // Sessao com transacao ativa, ou null quando nao ha transacao no escopo.
    IClientSessionHandle? Current { get; }
}

public interface ITransacaoMongo : IMongoSessionAccessor
{
    // true quando a transacao nao foi aberta por este escopo de aplicacao: dentro de um consumer o filtro do
    // outbox do MassTransit a abre e quem a confirma ou aborta e ele.
    bool Ambiente { get; }

    Task IniciarAsync(CancellationToken cancellationToken);

    // Confirma so a transacao aberta por IniciarAsync; sem transacao ou com a do consumer e no-op.
    Task ConfirmarAsync(CancellationToken cancellationToken);

    // Aborta so a transacao aberta por IniciarAsync; tolera transacao que o servidor ja abortou.
    Task AbortarAsync();
}

public sealed class TransacaoDoMassTransit(MongoDbContext contexto) : ITransacaoMongo
{
    private bool _abertaAqui;

    public IClientSessionHandle? Current => contexto.Session is { IsInTransaction: true } sessao ? sessao : null;

    public bool Ambiente => Current is not null && !_abertaAqui;

    public async Task IniciarAsync(CancellationToken cancellationToken)
    {
        await contexto.BeginTransaction(cancellationToken);
        _abertaAqui = true;
    }

    public async Task ConfirmarAsync(CancellationToken cancellationToken)
    {
        if (!_abertaAqui || Current is null) return;

        // O UoW sempre confirma explicitamente: publicar no outbox sem commit perde a mensagem em silencio.
        await contexto.CommitTransaction(cancellationToken);
        _abertaAqui = false;
    }

    public async Task AbortarAsync()
    {
        if (!_abertaAqui) return;

        _abertaAqui = false;
        try
        {
            await contexto.AbortTransaction(CancellationToken.None);
        }
        catch (Exception ex) when (ex is MongoException or InvalidOperationException)
        {
            // O servidor ja abortou a transacao (conflito de escrita, perda de primario): nada a desfazer.
        }
    }
}

// Sem outbox transacional (transporte/persistencia trocados nos testes): as escritas sao imediatas.
public sealed class SemTransacaoMongo : ITransacaoMongo
{
    public IClientSessionHandle? Current => null;

    public bool Ambiente => false;

    public Task IniciarAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ConfirmarAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AbortarAsync() => Task.CompletedTask;
}
