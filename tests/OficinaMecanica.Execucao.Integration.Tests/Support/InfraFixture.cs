using DotNet.Testcontainers.Builders;
using Testcontainers.MongoDb;
using Testcontainers.RabbitMq;

namespace OficinaMecanica.Execucao.Integration.Tests.Support;

public static class Colecoes
{
    // Os testes de infra compartilham os containers e rodam em serie: cada teste usa banco e virtual host proprios.
    public const string Infra = "Infra";
}

// O que a API de teste precisa saber da infraestrutura: o fixture compartilhado e a infraestrutura dedicada de um
// teste (que precisa parar containers) implementam o mesmo contrato.
public interface IEndpointsDeInfra
{
    string StringDeConexaoDoMongo { get; }
    string HostDoRabbit { get; }
    int PortaDoRabbit { get; }
    string UsuarioDoRabbit { get; }
    string SenhaDoRabbit { get; }

    Task<string> CriarVirtualHostAsync();
}

// Mongo 7 como replica set (o outbox transacional exige transacoes) e RabbitMQ, ambos com imagens pinadas e
// portas aleatorias do Testcontainers. Subem uma vez por execucao e sao removidos ao final.
public sealed class InfraFixture : IAsyncLifetime, IEndpointsDeInfra
{
    public const string ImagemMongo = "mongo:7.0.45";
    public const string ImagemRabbitMq = "rabbitmq:4.4.0-management";

    private const string UsuarioRabbit = "testes";
    // Credencial descartavel: o broker de teste vive so durante a execucao.
    private static readonly string SenhaRabbit = Guid.NewGuid().ToString("N");

    private readonly MongoDbContainer _mongo = new MongoDbBuilder(ImagemMongo)
        .WithReplicaSet()
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder(ImagemRabbitMq)
        .WithUsername(UsuarioRabbit)
        .WithPassword(SenhaRabbit)
        .Build();

    public string StringDeConexaoDoMongo => _mongo.GetConnectionString();

    public string HostDoRabbit => _rabbit.Hostname;

    public int PortaDoRabbit => _rabbit.GetMappedPublicPort(5672);

    public string UsuarioDoRabbit => UsuarioRabbit;

    public string SenhaDoRabbit => SenhaRabbit;

    public async Task InitializeAsync() =>
        await Task.WhenAll(_mongo.StartAsync(), _rabbit.StartAsync());

    public async Task DisposeAsync()
    {
        await _mongo.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    // Um virtual host por factory: o endpoint do consumer tem nome fixo (Filas.Execucao) e fabricas
    // diferentes disputariam as mesmas mensagens se compartilhassem o vhost.
    public async Task<string> CriarVirtualHostAsync()
    {
        var nome = "vh-" + Guid.NewGuid().ToString("N")[..10];

        var criar = await _rabbit.ExecAsync(["rabbitmqctl", "add_vhost", nome]);
        var permissoes = await _rabbit.ExecAsync(["rabbitmqctl", "set_permissions", "-p", nome, UsuarioRabbit, ".*", ".*", ".*"]);

        if (criar.ExitCode != 0 || permissoes.ExitCode != 0)
            throw new InvalidOperationException($"Falha ao criar o virtual host {nome}: {criar.Stderr} {permissoes.Stderr}");

        return nome;
    }
}

[CollectionDefinition(Colecoes.Infra)]
public sealed class InfraCollection : ICollectionFixture<InfraFixture>;

// Infraestrutura propria de UM teste, para os cenarios que param o RabbitMQ ou o Mongo sem afetar o fixture
// compartilhado. Sobe um Mongo (replica set) e um RabbitMQ novos e os remove no final.
public sealed class InfraDedicada : IAsyncLifetime, IEndpointsDeInfra
{
    private const string UsuarioRabbit = "testes";

    // Credencial descartavel: o broker de teste vive so durante o teste.
    private static readonly string SenhaRabbit = Guid.NewGuid().ToString("N");

    private readonly MongoDbContainer _mongo = new MongoDbBuilder(InfraFixture.ImagemMongo).WithReplicaSet().Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder(InfraFixture.ImagemRabbitMq)
        .WithUsername(UsuarioRabbit)
        .WithPassword(SenhaRabbit)
        .Build();

    public string StringDeConexaoDoMongo => _mongo.GetConnectionString();
    public string HostDoRabbit => _rabbit.Hostname;
    public int PortaDoRabbit => _rabbit.GetMappedPublicPort(5672);
    public string UsuarioDoRabbit => UsuarioRabbit;
    public string SenhaDoRabbit => SenhaRabbit;

    public async Task InitializeAsync() => await Task.WhenAll(_mongo.StartAsync(), _rabbit.StartAsync());

    public Task PararRabbitAsync() => _rabbit.StopAsync();

    public Task PararMongoAsync() => _mongo.StopAsync();

    public async Task DisposeAsync()
    {
        await _mongo.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    public async Task<string> CriarVirtualHostAsync()
    {
        var nome = "vh-" + Guid.NewGuid().ToString("N")[..10];

        var criar = await _rabbit.ExecAsync(["rabbitmqctl", "add_vhost", nome]);
        var permissoes = await _rabbit.ExecAsync(["rabbitmqctl", "set_permissions", "-p", nome, UsuarioRabbit, ".*", ".*", ".*"]);

        if (criar.ExitCode != 0 || permissoes.ExitCode != 0)
            throw new InvalidOperationException($"Falha ao criar o virtual host {nome}: {criar.Stderr} {permissoes.Stderr}");

        return nome;
    }
}
