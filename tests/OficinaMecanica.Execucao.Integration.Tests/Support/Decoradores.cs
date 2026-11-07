using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Integration.Tests.Support;

public static class DecoradoresExtensions
{
    // Decorator manual (sem biblioteca) sobre o registro existente de TServico, preservando o ciclo de vida.
    public static IServiceCollection Decorar<TServico>(
        this IServiceCollection services, Func<TServico, IServiceProvider, TServico> decorar)
        where TServico : class
    {
        var original = services.Last(d => d.ServiceType == typeof(TServico));
        services.Remove(original);
        services.Add(new ServiceDescriptor(
            typeof(TServico),
            sp =>
            {
                var interno = (TServico)(original.ImplementationInstance
                                         ?? original.ImplementationFactory?.Invoke(sp)
                                         ?? ActivatorUtilities.CreateInstance(sp, original.ImplementationType!));
                return decorar(interno, sp);
            },
            original.Lifetime));

        return services;
    }
}

public sealed class FalhaInjetadaException() : Exception("Falha injetada pelo teste.");

// Interruptor compartilhado entre o teste e os decoradores, que vivem em escopos diferentes.
public sealed class InterruptorDeFalha
{
    private volatile bool _ligado;

    public bool Ligado => _ligado;

    public void Ligar() => _ligado = true;

    public void Desligar() => _ligado = false;
}

// Lanca DEPOIS de publicar: o documento ja foi escrito e a mensagem ja esta no outbox, dentro da transacao.
public sealed class PublicadorQueFalhaAposPublicar(IIntegrationEventPublisher interno, InterruptorDeFalha falha)
    : IIntegrationEventPublisher
{
    public async Task PublishAsync<T>(T mensagem, CancellationToken cancellationToken = default) where T : class
    {
        await interno.PublishAsync(mensagem, cancellationToken);

        if (falha.Ligado)
            throw new FalhaInjetadaException();
    }
}

public sealed class UnitOfWorkQueFalha(IUnitOfWork interno, InterruptorDeFalha falha, ContadorDeChamadas contador) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        contador.Registrar();

        if (falha.Ligado)
            throw new FalhaInjetadaException();

        await interno.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ContadorDeChamadas
{
    private int _total;

    public int Total => Volatile.Read(ref _total);

    public void Registrar() => Interlocked.Increment(ref _total);
}

// Ponto de encontro para forcar duas operacoes concorrentes sobre a mesma ordem: cada Id so tem um
// encontro; depois dele (por exemplo, numa retentativa) as chamadas passam direto.
public sealed class PontoDeEncontro
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Barrier> _encontros = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> _chegadas = new();
    private readonly TimeSpan _limite;

    public PontoDeEncontro(TimeSpan? limite = null) => _limite = limite ?? TimeSpan.FromSeconds(10);

    private int _encontrosCompletos;

    public int EncontrosCompletos => Volatile.Read(ref _encontrosCompletos);

    public async Task AguardarAsync(Guid id)
    {
        var chegada = _chegadas.AddOrUpdate(id, 1, (_, atual) => atual + 1);
        if (chegada > 2)
            return;

        var barreira = _encontros.GetOrAdd(id, _ => new Barrier(2));
        var concluiu = await Task.Run(() => barreira.SignalAndWait(_limite));

        if (concluiu && chegada == 1)
            Interlocked.Increment(ref _encontrosCompletos);
    }
}

// Segura o fluxo das duas mensagens logo depois das pre-checagens do handler, antes de qualquer escrita.
public sealed class TombstonesComEncontro(ICancelamentoPrevioRepository interno, PontoDeEncontro encontro)
    : ICancelamentoPrevioRepository
{
    public async Task<CancelamentoPrevio?> ObterAsync(Guid ordemDeServicoId, CancellationToken cancellationToken = default)
    {
        var resultado = await interno.ObterAsync(ordemDeServicoId, cancellationToken);
        await encontro.AguardarAsync(ordemDeServicoId);
        return resultado;
    }

    public Task<bool> AdicionarAsync(CancelamentoPrevio cancelamento, CancellationToken cancellationToken = default) =>
        interno.AdicionarAsync(cancelamento, cancellationToken);
}

// Registra o desfecho de cada insercao de ordem (a colisao de chave e o que resolve a corrida).
public sealed class OrdensQueRegistramInsercoes(IOrdemDeProducaoRepository interno, RegistroDeInsercoes registro)
    : IOrdemDeProducaoRepository
{
    public Task<OrdemDeProducao?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        interno.ObterPorIdAsync(id, cancellationToken);

    public async Task<bool> AdicionarAsync(OrdemDeProducao ordem, CancellationToken cancellationToken = default)
    {
        try
        {
            var inserida = await interno.AdicionarAsync(ordem, cancellationToken);
            registro.Registrar(ordem.Id, inserida ? "inserida" : "ja-existia");
            return inserida;
        }
        catch (Exception ex)
        {
            registro.Registrar(ordem.Id, "colisao:" + ex.GetType().Name);
            throw;
        }
    }

    public Task AtualizarAsync(OrdemDeProducao ordem, CancellationToken cancellationToken = default) =>
        interno.AtualizarAsync(ordem, cancellationToken);

    public Task<OrdemDeProducao?> ObterProximaDaFilaAsync(StatusProducao status, CancellationToken cancellationToken = default) =>
        interno.ObterProximaDaFilaAsync(status, cancellationToken);

    public Task<IReadOnlyList<OrdemDeProducao>> ListarAsync(
        StatusProducao? status, string? mecanicoId, int pagina, int tamanho, CancellationToken cancellationToken = default) =>
        interno.ListarAsync(status, mecanicoId, pagina, tamanho, cancellationToken);

    public Task<int> ContarAsync(StatusProducao? status, string? mecanicoId, CancellationToken cancellationToken = default) =>
        interno.ContarAsync(status, mecanicoId, cancellationToken);

    public Task<IReadOnlyDictionary<StatusProducao, int>> ContarPorStatusAsync(CancellationToken cancellationToken = default) =>
        interno.ContarPorStatusAsync(cancellationToken);
}

public sealed class TombstonesQueRegistramInsercoes(ICancelamentoPrevioRepository interno, RegistroDeInsercoes registro)
    : ICancelamentoPrevioRepository
{
    public Task<CancelamentoPrevio?> ObterAsync(Guid ordemDeServicoId, CancellationToken cancellationToken = default) =>
        interno.ObterAsync(ordemDeServicoId, cancellationToken);

    public async Task<bool> AdicionarAsync(CancelamentoPrevio cancelamento, CancellationToken cancellationToken = default)
    {
        try
        {
            var inserido = await interno.AdicionarAsync(cancelamento, cancellationToken);
            registro.Registrar(cancelamento.OrdemDeServicoId, inserido ? "tombstone-inserido" : "tombstone-ja-existia");
            return inserido;
        }
        catch (Exception ex)
        {
            registro.Registrar(cancelamento.OrdemDeServicoId, "colisao:" + ex.GetType().Name);
            throw;
        }
    }
}

public sealed class RegistroDeInsercoes
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(Guid Id, string Desfecho)> _registros = new();

    public void Registrar(Guid id, string desfecho) => _registros.Enqueue((id, desfecho));

    public IReadOnlyList<string> De(Guid id) => _registros.Where(r => r.Id == id).Select(r => r.Desfecho).ToList();

    public int Colisoes => _registros.Count(r => r.Desfecho.StartsWith("colisao:"));
}
