using System.Collections.Concurrent;
using MassTransit;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Contracts;
using OficinaMecanica.Contracts.Commands;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Integration.Tests.Support;
using OficinaMecanica.Execucao.Application.Commands.EnfileirarOrdemDeProducao;
using OficinaMecanica.Execucao.Application.Commands.LiberarParaExecucao;

namespace OficinaMecanica.Execucao.Integration.Tests.Messaging;

// A composicao real do AddMensageria (endpoint Filas.Execucao, consumers e politica de retry) sobre o
// transporte em memoria e fakes de persistencia: valida o roteamento e o que e ou nao retentado, sem Docker.
public class ConsumerEndpointTests : IDisposable
{
    private sealed class ContadorDeRequisicoes
    {
        private readonly ConcurrentDictionary<string, int> _porTipo = new();

        public int De<T>() => _porTipo.GetValueOrDefault(typeof(T).Name);

        public void Registrar(string tipo) => _porTipo.AddOrUpdate(tipo, 1, (_, atual) => atual + 1);
    }

    // Registrado antes dos demais: conta toda tentativa, inclusive as que a validacao barra.
    private sealed class ContaTentativasBehavior<TRequest, TResponse>(ContadorDeRequisicoes contador)
        : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            contador.Registrar(typeof(TRequest).Name);
            return next();
        }
    }

    private sealed class RepositorioQueFalhaAsPrimeiras(IOrdemDeProducaoRepository interno, int falhas, Exception excecao)
        : IOrdemDeProducaoRepository
    {
        private int _chamadas;

        public Task<OrdemDeProducao?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref _chamadas) <= falhas ? throw excecao : interno.ObterPorIdAsync(id, cancellationToken);

        public Task<bool> AdicionarAsync(OrdemDeProducao ordem, CancellationToken cancellationToken = default) => interno.AdicionarAsync(ordem, cancellationToken);
        public Task AtualizarAsync(OrdemDeProducao ordem, CancellationToken cancellationToken = default) => interno.AtualizarAsync(ordem, cancellationToken);
        public Task<OrdemDeProducao?> ObterProximaDaFilaAsync(StatusProducao status, CancellationToken cancellationToken = default) => interno.ObterProximaDaFilaAsync(status, cancellationToken);
        public Task<IReadOnlyList<OrdemDeProducao>> ListarAsync(StatusProducao? status, string? mecanicoId, int pagina, int tamanho, CancellationToken cancellationToken = default) => interno.ListarAsync(status, mecanicoId, pagina, tamanho, cancellationToken);
        public Task<int> ContarAsync(StatusProducao? status, string? mecanicoId, CancellationToken cancellationToken = default) => interno.ContarAsync(status, mecanicoId, cancellationToken);
        public Task<IReadOnlyDictionary<StatusProducao, int>> ContarPorStatusAsync(CancellationToken cancellationToken = default) => interno.ContarPorStatusAsync(cancellationToken);
    }

    private readonly ContadorDeRequisicoes _contador = new();
    private ApiSemInfraFactory _api = null!;

    private ApiSemInfraFactory Subir(Action<IServiceCollection>? ajustes = null)
    {
        _api = new ApiSemInfraFactory(services =>
        {
            services.AddSingleton(_contador);
            services.Insert(0, ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(ContaTentativasBehavior<,>)));
            ajustes?.Invoke(services);
        });
        _ = _api.Server;
        return _api;
    }

    private async Task EnviarAsync<T>(T mensagem) where T : class
    {
        var endpoint = await _api.Services.GetRequiredService<IBus>().GetSendEndpoint(new Uri($"queue:{Filas.Execucao}"));
        await endpoint.Send(mensagem);
    }

    public void Dispose()
    {
        _api?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SolicitarDiagnostico_NoEndpointDaExecucao_DeveEnfileirarAOrdem()
    {
        Subir();
        var mensagem = new SolicitarDiagnosticoBuilder().Build();

        await EnviarAsync(mensagem);

        (await Esperar.AteAsync(() => _api.Ordens.Todas.Count == 1)).Should().BeTrue();
        var ordem = _api.Ordens.Todas.Single();
        ordem.Id.Should().Be(mensagem.OrdemDeServicoId);
        ordem.Status.Should().Be(StatusProducao.Fila);
        ordem.Numero.Should().Be(mensagem.Numero);
    }

    [Fact]
    public async Task IniciarExecucao_NoEndpointDaExecucao_DeveLiberarAOrdemComOSnapshot()
    {
        Subir();
        var ordem = new OrdemDeProducaoBuilder().DiagnosticoConcluido();
        _api.Ordens.Semear(ordem);

        await EnviarAsync(new IniciarExecucaoBuilder().ComOrdemDeServicoId(ordem.Id).Build());

        (await Esperar.AteAsync(() => ordem.Status == StatusProducao.AguardandoExecucao)).Should().BeTrue();
        ordem.LiberadaEm.Should().NotBeNull();
    }

    [Fact]
    public async Task CancelarProducao_NoEndpointDaExecucao_DeveCancelarAOrdemExistente()
    {
        Subir();
        var ordem = new OrdemDeProducaoBuilder().NaFila();
        _api.Ordens.Semear(ordem);

        await EnviarAsync(new CancelarProducaoBuilder().ComOrdemDeServicoId(ordem.Id).Build());

        (await Esperar.AteAsync(() => ordem.Status == StatusProducao.Cancelada)).Should().BeTrue();
    }

    [Fact]
    public async Task CancelarProducao_DeOrdemInexistente_DeveRegistrarTombstoneEImpedirOEnfileiramentoAtrasado()
    {
        Subir();
        var id = Guid.NewGuid();

        await EnviarAsync(new CancelarProducaoBuilder().ComOrdemDeServicoId(id).Build());
        (await Esperar.AteAsync(async () => await _api.Tombstones.ObterAsync(id) is not null)).Should().BeTrue();
        await EnviarAsync(new SolicitarDiagnosticoBuilder().ComOrdemDeServicoId(id).Build());
        (await Esperar.AteAsync(() => _contador.De<EnfileirarOrdemDeProducaoCommand>() == 1)).Should().BeTrue();
        await Task.Delay(300);

        _api.Ordens.Todas.Should().BeEmpty();
    }

    [Fact]
    public async Task DomainException_NaoDeveSerRetentada()
    {
        Subir();
        var ordem = new OrdemDeProducaoBuilder().NaFila();
        _api.Ordens.Semear(ordem);

        await EnviarAsync(new IniciarExecucaoBuilder().ComOrdemDeServicoId(ordem.Id).Build());

        (await Esperar.AteAsync(() => _contador.De<LiberarParaExecucaoCommand>() >= 1)).Should().BeTrue();
        await Task.Delay(600);
        _contador.De<LiberarParaExecucaoCommand>().Should().Be(1);
        ordem.Status.Should().Be(StatusProducao.Fila);
    }

    [Fact]
    public async Task ValidationException_NaoDeveSerRetentada()
    {
        Subir();
        var invalida = new SolicitarDiagnosticoBuilder().Build() with { Numero = "" };

        await EnviarAsync(invalida);

        (await Esperar.AteAsync(() => _contador.De<EnfileirarOrdemDeProducaoCommand>() >= 1)).Should().BeTrue();
        await Task.Delay(600);
        _contador.De<EnfileirarOrdemDeProducaoCommand>().Should().Be(1);
        _api.Ordens.Todas.Should().BeEmpty();
    }

    [Fact]
    public async Task FalhaTransitoria_DeveSerRetentadaAteConcluir()
    {
        Subir(services => services.Decorar<IOrdemDeProducaoRepository>((interno, _) =>
            new RepositorioQueFalhaAsPrimeiras(interno, falhas: 2, new InvalidOperationException("falha transitoria"))));
        var mensagem = new SolicitarDiagnosticoBuilder().Build();

        await EnviarAsync(mensagem);

        (await Esperar.AteAsync(() => _api.Ordens.Todas.Count == 1)).Should().BeTrue();
        _contador.De<EnfileirarOrdemDeProducaoCommand>()
            .Should().Be(3, "duas falhas e a tentativa que concluiu");
    }

    [Fact]
    public async Task FalhaTransitoriaPersistente_DeveEsgotarAsRetentativasConfiguradas()
    {
        Subir(services => services.Decorar<IOrdemDeProducaoRepository>((interno, _) =>
            new RepositorioQueFalhaAsPrimeiras(interno, falhas: int.MaxValue, new InvalidOperationException("banco fora"))));

        await EnviarAsync(new SolicitarDiagnosticoBuilder().Build());

        // 1 tentativa + 3 retentativas (Mensageria:Retentativas do ambiente de teste).
        (await Esperar.AteAsync(() => _contador.De<EnfileirarOrdemDeProducaoCommand>() >= 4)).Should().BeTrue();
        await Task.Delay(500);
        _contador.De<EnfileirarOrdemDeProducaoCommand>().Should().Be(4);
        _api.Ordens.Todas.Should().BeEmpty();
    }
}
