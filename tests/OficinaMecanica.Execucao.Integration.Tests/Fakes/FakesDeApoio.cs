using System.Collections.Concurrent;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Infrastructure.Observability;

namespace OficinaMecanica.Execucao.Integration.Tests.Fakes;

public sealed class FakeCancelamentoPrevioRepository : ICancelamentoPrevioRepository
{
    private readonly ConcurrentDictionary<Guid, CancelamentoPrevio> _tombstones = new();

    public Task<CancelamentoPrevio?> ObterAsync(Guid ordemDeServicoId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tombstones.GetValueOrDefault(ordemDeServicoId));

    public Task<bool> AdicionarAsync(CancelamentoPrevio cancelamento, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tombstones.TryAdd(cancelamento.OrdemDeServicoId, cancelamento));
}

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Salvamentos { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Salvamentos++;
        return Task.CompletedTask;
    }
}

public sealed class FakeIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly ConcurrentQueue<object> _publicadas = new();

    public IReadOnlyCollection<object> Publicadas => _publicadas.ToArray();

    public IEnumerable<T> De<T>() where T : class => _publicadas.OfType<T>();

    public Task PublishAsync<T>(T mensagem, CancellationToken cancellationToken = default) where T : class
    {
        _publicadas.Enqueue(mensagem);
        return Task.CompletedTask;
    }
}

public sealed class FakeIdadeDaFilaReader : IIdadeDaFilaReader
{
    public Task<IReadOnlyDictionary<StatusProducao, DateTimeOffset>> ObterInicioDaMaisAntigaAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<StatusProducao, DateTimeOffset>>(new Dictionary<StatusProducao, DateTimeOffset>());
}

public sealed class FakeOutboxPendenteReader : IOutboxPendenteReader
{
    public long Pendentes { get; set; }

    public Task<long> ContarAsync(CancellationToken cancellationToken) => Task.FromResult(Pendentes);
}
