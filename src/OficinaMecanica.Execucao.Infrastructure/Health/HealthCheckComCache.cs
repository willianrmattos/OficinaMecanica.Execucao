using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace OficinaMecanica.Execucao.Infrastructure.Health;

// Secao "Health". O cache existe porque /health/ready e anonimo e, atras do wildcard da APIM, qualquer cliente da internet
// o alcanca: sem cache, cada requisicao abriria uma conexao AMQP e um ping no Atlas (ate 3 s cada, com a dependencia
// congelada). Com ele o custo fica limitado a uma avaliacao de cada dependencia por janela, nao importa a taxa de requisicoes.
public sealed class OpcoesDaProntidao
{
    public const string Secao = "Health";
    public const int CachePadraoEmSegundos = 5;

    // Menor que o periodSeconds da readiness (30 s) de proposito: cada probe do Kubernetes cai fora da janela e ve um
    // resultado novo. Zero (ou negativo) desliga o cache; os testes de infraestrutura precisam ver cada mudanca na hora.
    public int ProntidaoCacheSegundos { get; set; } = CachePadraoEmSegundos;
}

// Ultimo resultado de um check e ate quando vale, compartilhado entre as chamadas (um por registro). A trava faz as
// requisicoes simultaneas esperarem a primeira avaliacao em vez de abrirem uma conexao cada.
public sealed class EstadoDoCacheDeProntidao
{
    internal SemaphoreSlim Trava { get; } = new(1, 1);
    internal HealthCheckResult? Resultado { get; set; }
    internal DateTimeOffset ValidoAte { get; set; }
}

public sealed class HealthCheckComCache : IHealthCheck
{
    private readonly IHealthCheck _interno;
    private readonly EstadoDoCacheDeProntidao _estado;
    private readonly IOptions<OpcoesDaProntidao> _opcoes;
    private readonly TimeProvider _relogio;

    public HealthCheckComCache(
        IHealthCheck interno, EstadoDoCacheDeProntidao estado, IOptions<OpcoesDaProntidao> opcoes, TimeProvider relogio)
    {
        _interno = interno;
        _estado = estado;
        _opcoes = opcoes;
        _relogio = relogio;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var validade = TimeSpan.FromSeconds(Math.Max(0, _opcoes.Value.ProntidaoCacheSegundos));
        if (validade == TimeSpan.Zero)
            return await _interno.CheckHealthAsync(context, cancellationToken);

        await _estado.Trava.WaitAsync(cancellationToken);
        try
        {
            if (_estado.Resultado is { } guardado && _relogio.GetUtcNow() < _estado.ValidoAte)
                return guardado;

            // Uma excecao (o chamador cancelou) sobe sem gravar nada: o cache so guarda uma medicao que terminou.
            var resultado = await _interno.CheckHealthAsync(context, cancellationToken);
            _estado.Resultado = resultado;
            _estado.ValidoAte = _relogio.GetUtcNow() + validade;
            return resultado;
        }
        finally
        {
            _estado.Trava.Release();
        }
    }
}

public static class HealthChecksComCacheExtensions
{
    // Registra o check real (criado a cada avaliacao, no escopo da propria, como o AddCheck<T> faz) dentro do cache. O
    // estado do cache e um so por registro: e criado aqui, uma vez, e capturado pela fabrica.
    public static IHealthChecksBuilder AddCheckComCache<TCheck>(
        this IHealthChecksBuilder builder, string nome, HealthStatus? failureStatus, IEnumerable<string> tags)
        where TCheck : class, IHealthCheck
    {
        var estado = new EstadoDoCacheDeProntidao();

        return builder.Add(new HealthCheckRegistration(
            nome,
            provedor => new HealthCheckComCache(
                ActivatorUtilities.GetServiceOrCreateInstance<TCheck>(provedor),
                estado,
                provedor.GetRequiredService<IOptions<OpcoesDaProntidao>>(),
                provedor.GetService<TimeProvider>() ?? TimeProvider.System),
            failureStatus,
            tags));
    }

    // Idempotente: AddPersistenciaMongo e AddMensageria podem ser chamados sozinhos (testes) e os dois precisam da secao.
    public static IServiceCollection AddOpcoesDaProntidao(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OpcoesDaProntidao>().Bind(configuration.GetSection(OpcoesDaProntidao.Secao));
        return services;
    }
}
