using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using OficinaMecanica.Contracts;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Infrastructure.Authentication;
using OficinaMecanica.Execucao.Infrastructure.Health;
using OficinaMecanica.Execucao.Infrastructure.Messaging;
using OficinaMecanica.Execucao.Infrastructure.Messaging.Consumers;
using OficinaMecanica.Execucao.Infrastructure.Observability;
using OficinaMecanica.Execucao.Infrastructure.Persistence;

namespace OficinaMecanica.Execucao.Infrastructure;

public static class DependencyInjection
{
    // A ordem importa: a validacao das opcoes vem primeiro, para uma configuracao invalida derrubar a partida antes
    // de qualquer conexao; o MongoInitializer (hosted service) precisa ser registrado ANTES do bus, para criar
    // colecoes e indices antes de a primeira mensagem ser consumida.
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpcoesDaMensageria(configuration);
        services.AddPersistenciaMongo(configuration);
        services.AddMensageria(configuration);
        services.AddMetricasDaFila(configuration);
        services.AddAutenticacaoJwt(configuration);

        return services;
    }

    public static IServiceCollection AddPersistenciaMongo(this IServiceCollection services, IConfiguration configuration)
    {
        var stringDeConexao = configuration.GetConnectionString("MongoDb");
        if (string.IsNullOrWhiteSpace(stringDeConexao))
            throw new InvalidOperationException("ConnectionStrings:MongoDb não configurada.");

        var nomeDoBanco = configuration["MongoDb:Database"] ?? "execucao";
        var mensageria = MensageriaOptions.Carregar(configuration);

        // Antes de criar o MongoClient: os mapeamentos congelam no primeiro uso do driver.
        MongoMappings.Registrar();

        // Um MongoClient singleton: ele e quem gerencia o pool de conexoes (o M0 do Atlas limita 500).
        services.TryAddSingleton<IMongoClient>(_ =>
        {
            var configuracao = MongoClientSettings.FromConnectionString(stringDeConexao);
            configuracao.ApplicationName = "OficinaMecanica.Execucao";
            return new MongoClient(configuracao);
        });
        services.TryAddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(nomeDoBanco));

        services.AddSingleton(new MongoInitializerOptions
        {
            PreCriarColecoesDoOutbox = mensageria.UsaOutboxDoMongo
        });
        services.AddHostedService<MongoInitializer>();

        if (mensageria.UsaOutboxDoMongo)
            services.AddScoped<ITransacaoMongo, TransacaoDoMassTransit>();
        else
            services.AddScoped<ITransacaoMongo, SemTransacaoMongo>();
        services.AddScoped<IMongoSessionAccessor>(sp => sp.GetRequiredService<ITransacaoMongo>());

        services.AddScoped<IMetricasPendentes, MetricasPendentes>();
        services.AddScoped<IOrdemDeProducaoRepository, OrdemDeProducaoRepository>();
        services.AddScoped<ICancelamentoPrevioRepository, CancelamentoPrevioRepository>();
        services.AddScoped<IUnitOfWork, MongoUnitOfWork>();

        // Depois do AddApplication, que registra o ValidationBehavior: a validacao roda por fora, sem
        // abrir transacao para um command invalido.
        services.TryAddSingleton(new TransacaoOptions());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransacaoBehavior<,>));

        // O resultado de cada check de prontidao fica em cache por alguns segundos porque /health/ready e anonimo
        // (ver OpcoesDaProntidao).
        services.AddOpcoesDaProntidao(configuration);
        services.AddHealthChecks().AddCheckComCache<MongoHealthCheck>("mongodb", failureStatus: null, tags: ["ready"]);

        return services;
    }

    // Pontos de troca para teste: Mensageria:Transporte (RabbitMq | EmMemoria) e Mensageria:Outbox
    // (MongoDb | Nenhum); configurarBus permite registrar consumers/harness adicionais.
    public static IServiceCollection AddMensageria(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configurarBus = null)
    {
        var opcoes = MensageriaOptions.Carregar(configuration);
        var rabbitMq = MensageriaOptions.CarregarRabbitMq(configuration);

        services.AddOpcoesDaMensageria(configuration);
        services.AddSingleton(opcoes);
        services.AddScoped<IIntegrationEventPublisher, MassTransitIntegrationEventPublisher>();

        services.AddOpcoesDaProntidao(configuration);
        if (opcoes.UsaRabbitMq)
            services.AddHealthChecks().AddCheckComCache<RabbitMqHealthCheck>("rabbitmq", failureStatus: null, tags: ["ready"]);

        // O check do bus que o MassTransit registra tambem nao pode tirar o pod de rotacao quando o broker cair:
        // com o outbox o servico continua aceitando escrita. Rebaixo a falha dele para Degraded (HTTP 200).
        services.PostConfigure<HealthCheckServiceOptions>(health =>
        {
            foreach (var registro in health.Registrations.Where(r => r.Name == "masstransit-bus"))
                registro.FailureStatus = HealthStatus.Degraded;
        });

        // O host so fica pronto depois de o bus subir: sem isso a API aceitaria requisicoes antes de poder
        // publicar e consumir.
        services.AddOptions<MassTransitHostOptions>().Configure(host => host.WaitUntilStarted = true);

        services.AddMassTransit(x =>
        {
            x.AddConsumer<SolicitarDiagnosticoConsumer>();
            x.AddConsumer<IniciarExecucaoConsumer>();
            x.AddConsumer<CancelarProducaoConsumer>();

            configurarBus?.Invoke(x);

            if (opcoes.UsaOutboxDoMongo)
            {
                x.AddMongoDbOutbox(outbox =>
                {
                    outbox.ClientFactory(sp => sp.GetRequiredService<IMongoClient>());
                    outbox.DatabaseFactory(sp => sp.GetRequiredService<IMongoDatabase>());
                    outbox.QueryDelay = TimeSpan.FromMilliseconds(opcoes.IntervaloDoOutboxEmMs);
                    outbox.DuplicateDetectionWindow = TimeSpan.FromMinutes(opcoes.JanelaDeDeduplicacaoEmMinutos);

                    // Fora de consumer (caminho HTTP) o IPublishEndpoint escopado grava no outbox, na
                    // transacao aberta pelo TransacaoBehavior.
                    outbox.UseBusOutbox();
                });
            }

            if (opcoes.UsaRabbitMq)
            {
                x.UsingRabbitMq((contexto, bus) =>
                {
                    bus.Host(rabbitMq.Host, rabbitMq.Port, rabbitMq.VirtualHost, host =>
                    {
                        host.Username(rabbitMq.Username);
                        host.Password(rabbitMq.Password);
                    });

                    ConfigurarEndpointDeExecucao(contexto, bus, opcoes);
                });
            }
            else
            {
                x.UsingInMemory((contexto, bus) => ConfigurarEndpointDeExecucao(contexto, bus, opcoes));
            }
        });

        return services;
    }

    // Chamado por AddInfrastructure (antes de tudo) e por AddMensageria; o registro e idempotente.
    public static IServiceCollection AddOpcoesDaMensageria(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection(RabbitMqOptions.Secao));
        services.AddOptions<MensageriaOptions>()
            .Bind(configuration.GetSection(MensageriaOptions.Secao))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MensageriaOptions>, ValidadorDasOpcoesDaMensageria>());

        return services;
    }

    public static IServiceCollection AddMetricasDaFila(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton(new MetricasDaFilaOptions
        {
            IntervaloDeAtualizacaoEmSegundos =
                configuration.GetValue("Metricas:IntervaloDeAtualizacaoEmSegundos", new MetricasDaFilaOptions().IntervaloDeAtualizacaoEmSegundos)
        });
        services.AddSingleton<MetricasDaFila>();
        services.AddSingleton<IIdadeDaFilaReader, MongoIdadeDaFilaReader>();
        services.AddSingleton<IOutboxPendenteReader, MongoOutboxPendenteReader>();
        services.AddHostedService<MetricasDaFilaBackgroundService>();

        return services;
    }

    private static void ConfigurarEndpointDeExecucao(
        IBusRegistrationContext contexto, IBusFactoryConfigurator bus, MensageriaOptions opcoes)
    {
        bus.ReceiveEndpoint(Filas.Execucao, endpoint =>
        {
            // O retry fica no endpoint, POR FORA do filtro do outbox: cada tentativa reabre a transacao e uma falha
            // transitoria do proprio outbox (registro no inbox, abertura ou commit da transacao) e retentada em vez de
            // ir para _error. Registro o retry antes, como o MassTransit recomenda, mas no 8.5.12 o UseMongoDbOutbox
            // entra como filtro do consumer, entao a ordem das duas chamadas e indiferente (a pilha de chamadas mostra
            // o RetryFilter por fora nos dois casos); o que quebra e mover o retry para dentro do consumer, e um teste
            // de infra cobre isso injetando a falha no filtro do outbox. Excecao de negocio e de validacao nao melhora
            // com nova tentativa e vai direto para _error.
            endpoint.UseMessageRetry(retry =>
            {
                retry.Intervals(opcoes.IntervalosDeRetentativa().ToArray());
                retry.Ignore<ValidationException>();
                retry.Ignore<DomainException>();
            });

            if (opcoes.UsaOutboxDoMongo)
                endpoint.UseMongoDbOutbox(contexto);

            endpoint.ConfigureConsumer<SolicitarDiagnosticoConsumer>(contexto);
            endpoint.ConfigureConsumer<IniciarExecucaoConsumer>(contexto);
            endpoint.ConfigureConsumer<CancelarProducaoConsumer>(contexto);
        });
    }
}
