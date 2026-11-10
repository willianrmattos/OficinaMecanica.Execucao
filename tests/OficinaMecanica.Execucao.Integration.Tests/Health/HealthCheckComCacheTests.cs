using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using OficinaMecanica.Execucao.Infrastructure;
using OficinaMecanica.Execucao.Infrastructure.Health;
using OficinaMecanica.Execucao.Infrastructure.Persistence;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Health;

// /health/ready e anonimo e passa pelo wildcard da APIM: o cache limita o custo (uma conexao AMQP e um ping no Atlas por
// janela) a qualquer taxa de requisicoes. Sem Docker: o check interno e um contador e o relogio e controlado.
public class HealthCheckComCacheTests
{
    private static readonly HealthCheckContext Contexto = new()
    {
        Registration = new HealthCheckRegistration("teste", _ => null!, null, null)
    };

    private sealed class CheckContador : IHealthCheck
    {
        private int _chamadas;

        public int Chamadas => Volatile.Read(ref _chamadas);

        public Func<int, Task<HealthCheckResult>> Resultado { get; set; } =
            n => Task.FromResult(HealthCheckResult.Healthy($"avaliacao {n}"));

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            await Resultado(Interlocked.Increment(ref _chamadas));
    }

    private static HealthCheckComCache Criar(CheckContador interno, RelogioControlado relogio, int cacheSegundos = 5) =>
        new(interno, new EstadoDoCacheDeProntidao(),
            Options.Create(new OpcoesDaProntidao { ProntidaoCacheSegundos = cacheSegundos }), relogio);

    private static async Task EsperarPrimeiraAvaliacaoAsync(CheckContador interno)
    {
        var limite = DateTime.UtcNow.AddSeconds(10);
        while (interno.Chamadas == 0 && DateTime.UtcNow < limite)
            await Task.Delay(10);
    }

    [Fact]
    public void Opcoes_SemConfiguracao_DevemTerCacheDeCincoSegundos()
    {
        new OpcoesDaProntidao().ProntidaoCacheSegundos.Should().Be(5);
    }

    [Fact]
    public async Task SegundaChamadaDentroDaValidade_DeveDevolverOResultadoGuardadoSemAvaliarDeNovo()
    {
        var interno = new CheckContador();
        var check = Criar(interno, new RelogioControlado());

        var primeira = await check.CheckHealthAsync(Contexto);
        var segunda = await check.CheckHealthAsync(Contexto);

        interno.Chamadas.Should().Be(1);
        primeira.Description.Should().Be("avaliacao 1");
        segunda.Description.Should().Be("avaliacao 1");
    }

    [Fact]
    public async Task AoFimDaValidade_DeveAvaliarDeNovoEGuardarOResultadoNovo()
    {
        var interno = new CheckContador();
        var relogio = new RelogioControlado();
        var check = Criar(interno, relogio);
        await check.CheckHealthAsync(Contexto);

        relogio.Avancar(TimeSpan.FromSeconds(4.9));
        (await check.CheckHealthAsync(Contexto)).Description.Should().Be("avaliacao 1", "ainda dentro dos 5 s");

        relogio.Avancar(TimeSpan.FromSeconds(0.1));
        var renovada = await check.CheckHealthAsync(Contexto);
        var seguinte = await check.CheckHealthAsync(Contexto);

        interno.Chamadas.Should().Be(2);
        renovada.Description.Should().Be("avaliacao 2");
        seguinte.Description.Should().Be("avaliacao 2", "a medicao nova tambem fica guardada");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task ComCacheDesligado_DeveAvaliarEmTodaChamada(int segundos)
    {
        var interno = new CheckContador();
        var check = Criar(interno, new RelogioControlado(), segundos);

        await check.CheckHealthAsync(Contexto);
        var segunda = await check.CheckHealthAsync(Contexto);

        interno.Chamadas.Should().Be(2);
        segunda.Description.Should().Be("avaliacao 2");
    }

    [Fact]
    public async Task ResultadoUnhealthy_TambemFicaGuardado_EALimitaACargaSobreODependenteForaDoAr()
    {
        var interno = new CheckContador { Resultado = _ => Task.FromResult(HealthCheckResult.Unhealthy("banco fora")) };
        var check = Criar(interno, new RelogioControlado());

        await check.CheckHealthAsync(Contexto);
        var segunda = await check.CheckHealthAsync(Contexto);

        interno.Chamadas.Should().Be(1);
        segunda.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task ChamadasSimultaneas_DevemSerRespondidasPelaMesmaAvaliacao()
    {
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interno = new CheckContador
        {
            Resultado = async n =>
            {
                await liberar.Task;
                return HealthCheckResult.Healthy($"avaliacao {n}");
            }
        };
        var check = Criar(interno, new RelogioControlado());

        var chamadas = Enumerable.Range(0, 30).Select(_ => Task.Run(() => check.CheckHealthAsync(Contexto))).ToArray();
        await EsperarPrimeiraAvaliacaoAsync(interno);
        liberar.SetResult();
        var resultados = await Task.WhenAll(chamadas).ComTetoAsync("as 30 chamadas devem terminar depois de liberada a avaliacao");

        interno.Chamadas.Should().Be(1, "as requisicoes simultaneas esperam a primeira avaliacao em vez de abrir uma conexao cada");
        resultados.Select(r => r.Description).Should().OnlyContain(d => d == "avaliacao 1");
    }

    [Fact]
    public async Task ExcecaoDoCheck_DevePropagarENaoSerGuardada()
    {
        var interno = new CheckContador
        {
            Resultado = n => n == 1
                ? throw new OperationCanceledException()
                : Task.FromResult(HealthCheckResult.Healthy($"avaliacao {n}"))
        };
        var check = Criar(interno, new RelogioControlado());

        var act = () => check.CheckHealthAsync(Contexto);
        await act.Should().ThrowAsync<OperationCanceledException>();
        var seguinte = await check.CheckHealthAsync(Contexto);

        interno.Chamadas.Should().Be(2, "uma medicao interrompida nao vira resposta guardada");
        seguinte.Description.Should().Be("avaliacao 2");
    }

    [Fact]
    public async Task ChamadorQueCancelaEnquantoEsperaAAvaliacaoEmAndamento_DevePropagarOCancelamento()
    {
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interno = new CheckContador
        {
            Resultado = async _ =>
            {
                await liberar.Task;
                return HealthCheckResult.Healthy();
            }
        };
        var check = Criar(interno, new RelogioControlado());
        var emAndamento = Task.Run(() => check.CheckHealthAsync(Contexto));
        await EsperarPrimeiraAvaliacaoAsync(interno);
        using var cancelamento = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Sem a trava (SemaphoreSlim.WaitAsync com o token) esta chamada avaliaria o check interno, que espera `liberar` sem
        // olhar o token: o teto faz o teste falhar em vez de pendurar.
        var act = () => check.CheckHealthAsync(Contexto, cancelamento.Token)
            .ComTetoAsync("o chamador que cancela enquanto espera a trava deve receber o cancelamento, nao ficar preso na avaliacao");

        try
        {
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            // Libero a avaliacao em andamento mesmo se a asseracao acima falhar, para nao deixar uma tarefa pendurada.
            liberar.TrySetResult();
        }

        await emAndamento.ComTetoAsync("a avaliacao em andamento deve terminar depois de liberada");
        interno.Chamadas.Should().Be(1);
    }

    // ---- Check real do MongoDB por dentro do cache ----

    // O ping fica preso ate o token ser cancelado e so na segunda avaliacao responde: e o contrato do driver quando o
    // chamador aborta a requisicao no meio (OperationCanceledException), sem precisar de um servidor de verdade.
    private static (Mock<IMongoDatabase> Banco, TaskCompletionSource Entrou, Func<int> Pings) BancoComPingPresoNaPrimeiraChamada()
    {
        var entrou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pings = 0;
        var banco = new Mock<IMongoDatabase>();
        banco.Setup(b => b.RunCommandAsync(It.IsAny<Command<BsonDocument>>(), It.IsAny<ReadPreference>(), It.IsAny<CancellationToken>()))
            .Returns(async (Command<BsonDocument> _, ReadPreference _, CancellationToken token) =>
            {
                if (Interlocked.Increment(ref pings) == 1)
                {
                    entrou.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }

                return new BsonDocument("ok", 1);
            });
        return (banco, entrou, () => Volatile.Read(ref pings));
    }

    [Fact]
    public async Task CheckRealDoMongo_ComChamadorQueCancelaNoMeio_NaoDeveGuardarNadaEAChamadaSeguinteReavalia()
    {
        var (banco, entrou, pings) = BancoComPingPresoNaPrimeiraChamada();
        var check = new HealthCheckComCache(
            new MongoHealthCheck(banco.Object), new EstadoDoCacheDeProntidao(),
            Options.Create(new OpcoesDaProntidao()), new RelogioControlado());
        using var chamador = new CancellationTokenSource();

        var abortada = check.CheckHealthAsync(Contexto, chamador.Token);
        await entrou.Task.ComTetoAsync("o check do MongoDB deve chegar a consultar o banco");
        await chamador.CancelAsync();
        var acao = () => abortada.ComTetoAsync("o cancelamento do chamador deve interromper a consulta ao banco");
        await acao.Should().ThrowAsync<OperationCanceledException>("a requisicao abortada nao e uma medicao do banco");

        var seguinte = await check.CheckHealthAsync(Contexto).ComTetoAsync("a chamada seguinte deve reavaliar e terminar");

        pings().Should().Be(2, "o cancelamento nao deixou resultado guardado: a chamada seguinte consulta o banco de novo");
        seguinte.Status.Should().Be(HealthStatus.Healthy, "um 503 falso nao pode ficar no cache por causa de uma requisicao abortada");
    }

    // ---- Registro real ----

    private static ServiceProvider ProvedorComUmCheckEmCache(CheckContador interno, RelogioControlado relogio)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(relogio);
        services.AddSingleton(interno);
        services.AddOptions<OpcoesDaProntidao>();
        services.AddHealthChecks().AddCheckComCache<CheckContador>("dependencia", failureStatus: null, tags: ["ready"]);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AddCheckComCache_ComConfiguracaoPadrao_DeveGuardarEntreAvaliacoesDoHealthCheckService()
    {
        var interno = new CheckContador();
        var relogio = new RelogioControlado();
        await using var provedor = ProvedorComUmCheckEmCache(interno, relogio);
        var servico = provedor.GetRequiredService<HealthCheckService>();

        await servico.CheckHealthAsync();
        await servico.CheckHealthAsync();
        interno.Chamadas.Should().Be(1, "o estado do cache sobrevive aos escopos que o HealthCheckService cria a cada avaliacao");

        relogio.Avancar(TimeSpan.FromSeconds(5));
        await servico.CheckHealthAsync();
        interno.Chamadas.Should().Be(2);
    }

    [Fact]
    public async Task AddInfrastructure_DeveLerOCacheDaSecaoHealthERegistrarOsDoisChecksComCache()
    {
        async Task<(int Cache, IReadOnlyDictionary<string, Type> Checks)> LerAsync(params (string, string?)[] valores)
        {
            var configuracao = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoDb"] = "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=300",
                    ["JwtSettings:JwksUri"] = "https://exemplo.invalid/jwks.json",
                    ["Mensageria:Transporte"] = "RabbitMq",
                    ["Mensageria:Outbox"] = "MongoDb"
                }.Concat(valores.Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2)))).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddInfrastructure(configuracao);
            await using var provedor = services.BuildServiceProvider();
            var registros = provedor.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
            return (provedor.GetRequiredService<IOptions<OpcoesDaProntidao>>().Value.ProntidaoCacheSegundos,
                registros.Where(r => r.Name is "mongodb" or "rabbitmq").ToDictionary(r => r.Name, r => r.Factory(provedor).GetType()));
        }

        var padrao = await LerAsync();
        padrao.Cache.Should().Be(5);
        padrao.Checks.Keys.Should().BeEquivalentTo("mongodb", "rabbitmq");
        padrao.Checks.Values.Should().AllBeEquivalentTo(typeof(HealthCheckComCache), "o check real so roda por dentro do cache");
        (await LerAsync(("Health:ProntidaoCacheSegundos", "0"))).Cache.Should().Be(0);
        (await LerAsync(("Health:ProntidaoCacheSegundos", "12"))).Cache.Should().Be(12);
    }
}
