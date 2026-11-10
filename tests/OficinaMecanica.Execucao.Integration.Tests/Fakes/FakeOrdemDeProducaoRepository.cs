using System.Collections.Concurrent;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Integration.Tests.Fakes;

// Repositorio em memoria com a mesma semantica do contrato (checagem de Version, FIFO por EnfileiradaEm e
// LiberadaEm): permite exercitar controllers e handlers reais sem Docker. Guarda a propria instancia da
// ordem, entao os testes enxergam as mutacoes feitas pelos handlers.
public sealed class FakeOrdemDeProducaoRepository : IOrdemDeProducaoRepository
{
    private readonly ConcurrentDictionary<Guid, OrdemDeProducao> _ordens = new();
    private readonly object _trava = new();

    public IReadOnlyCollection<OrdemDeProducao> Todas => _ordens.Values.ToList();

    public void Semear(OrdemDeProducao ordem) => _ordens[ordem.Id] = ordem;

    public Task<OrdemDeProducao?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_ordens.GetValueOrDefault(id));

    public Task<bool> AdicionarAsync(OrdemDeProducao ordem, CancellationToken cancellationToken = default) =>
        Task.FromResult(_ordens.TryAdd(ordem.Id, ordem));

    public Task AtualizarAsync(OrdemDeProducao ordem, CancellationToken cancellationToken = default)
    {
        lock (_trava)
        {
            if (!_ordens.TryGetValue(ordem.Id, out var persistida))
                throw new ConcorrenciaException("Ordem inexistente.");

            if (persistida.Version != ordem.Version)
                throw new ConcorrenciaException("Version desatualizada.");

            ordem.IncrementVersion();
            _ordens[ordem.Id] = ordem;
        }

        return Task.CompletedTask;
    }

    public Task<OrdemDeProducao?> ObterProximaDaFilaAsync(StatusProducao status, CancellationToken cancellationToken = default) =>
        Task.FromResult(Ordenar(_ordens.Values.Where(o => o.Status == status), status).FirstOrDefault());

    public Task<IReadOnlyList<OrdemDeProducao>> ListarAsync(
        StatusProducao? status, string? mecanicoId, int pagina, int tamanho, CancellationToken cancellationToken = default)
    {
        var filtradas = Filtrar(status, mecanicoId);
        var ordenadas = status.HasValue ? Ordenar(filtradas, status.Value) : filtradas.OrderBy(o => o.EnfileiradaEm);

        return Task.FromResult<IReadOnlyList<OrdemDeProducao>>(ordenadas.Skip((pagina - 1) * tamanho).Take(tamanho).ToList());
    }

    public Task<int> ContarAsync(StatusProducao? status, string? mecanicoId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Filtrar(status, mecanicoId).Count());

    public Task<IReadOnlyDictionary<StatusProducao, int>> ContarPorStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<StatusProducao, int>>(
            _ordens.Values.GroupBy(o => o.Status).ToDictionary(g => g.Key, g => g.Count()));

    private IEnumerable<OrdemDeProducao> Filtrar(StatusProducao? status, string? mecanicoId) =>
        _ordens.Values
            .Where(o => !status.HasValue || o.Status == status.Value)
            .Where(o => string.IsNullOrWhiteSpace(mecanicoId) || o.MecanicoId == mecanicoId);

    private static IOrderedEnumerable<OrdemDeProducao> Ordenar(IEnumerable<OrdemDeProducao> ordens, StatusProducao status) =>
        status == StatusProducao.AguardandoExecucao
            ? ordens.OrderBy(o => o.LiberadaEm)
            : ordens.OrderBy(o => o.EnfileiradaEm);
}
