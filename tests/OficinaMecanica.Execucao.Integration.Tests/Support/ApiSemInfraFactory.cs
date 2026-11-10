using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Infrastructure.Observability;
using OficinaMecanica.Execucao.Infrastructure.Persistence;
using OficinaMecanica.Execucao.Integration.Tests.Fakes;

namespace OficinaMecanica.Execucao.Integration.Tests.Support;

// API sem Docker: persistencia e publicador trocados por fakes em memoria e transporte do MassTransit em
// memoria, sem outbox. Exercita controllers, middleware, auth, validacao e handlers reais.
public sealed class ApiSemInfraFactory : ExecucaoApiFactoryBase
{
    private readonly Action<IServiceCollection>? _ajustes;
    private readonly string? _jwksUriReal;
    private readonly string _ambiente;
    private readonly IReadOnlyDictionary<string, string> _configuracao;

    public ApiSemInfraFactory(
        Action<IServiceCollection>? ajustes = null,
        string? jwksUriReal = null,
        string ambiente = "Testing",
        IReadOnlyDictionary<string, string>? configuracao = null)
    {
        _ajustes = ajustes;
        _jwksUriReal = jwksUriReal;
        _ambiente = ambiente;
        _configuracao = configuracao ?? new Dictionary<string, string>();
    }

    protected override bool UsaChaveLocal => _jwksUriReal is null;

    public FakeOrdemDeProducaoRepository Ordens { get; } = new();
    public FakeCancelamentoPrevioRepository Tombstones { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeIntegrationEventPublisher Publicador { get; } = new();

    protected override void ConfigurarAmbiente(IWebHostBuilder builder)
    {
        // O driver nunca conecta (so a readiness resolve o IMongoDatabase): a selecao de servidor curta
        // faz o health check de prontidao falhar rapido.
        builder.UseSetting("ConnectionStrings:MongoDb", "mongodb://localhost:1/?serverSelectionTimeoutMS=300&connectTimeoutMS=300");
        if (_jwksUriReal is not null)
            builder.UseSetting("JwtSettings:JwksUri", _jwksUriReal);

        builder.UseSetting("Mensageria:Transporte", "EmMemoria");
        builder.UseSetting("Mensageria:Outbox", "Nenhum");
        builder.UseSetting("Mensageria:Retentativas", "3");
        builder.UseSetting("Mensageria:IntervaloInicialEmMs", "20");
        builder.UseSetting("Mensageria:IntervaloMaximoEmMs", "50");
        builder.UseEnvironment(_ambiente);

        foreach (var (chave, valor) in _configuracao)
            builder.UseSetting(chave, valor);

        builder.ConfigureServices(services =>
        {
            // Sem Mongo: nao ha indices a criar nem leitura real do tamanho da fila.
            var inicializadores = services
                .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(MongoInitializer))
                .ToList();
            foreach (var inicializador in inicializadores)
                services.Remove(inicializador);

            services.RemoveAll<IIdadeDaFilaReader>();
            services.AddSingleton<IIdadeDaFilaReader, FakeIdadeDaFilaReader>();
            services.RemoveAll<IOutboxPendenteReader>();
            services.AddSingleton<IOutboxPendenteReader, FakeOutboxPendenteReader>();

            services.RemoveAll<IOrdemDeProducaoRepository>();
            services.AddSingleton<IOrdemDeProducaoRepository>(Ordens);
            services.RemoveAll<ICancelamentoPrevioRepository>();
            services.AddSingleton<ICancelamentoPrevioRepository>(Tombstones);
            services.RemoveAll<IUnitOfWork>();
            services.AddSingleton<IUnitOfWork>(UnitOfWork);
            services.RemoveAll<IIntegrationEventPublisher>();
            services.AddSingleton<IIntegrationEventPublisher>(Publicador);

            _ajustes?.Invoke(services);
        });
    }
}
