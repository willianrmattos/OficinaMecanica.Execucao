using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace OficinaMecanica.Execucao.Integration.Tests.Support;

// API inteira contra Mongo (replica set) e RabbitMQ reais dos containers: banco e virtual host exclusivos por
// instancia, para os testes nao interferirem entre si.
public sealed class ApiComInfraFactory : ExecucaoApiFactoryBase
{
    private readonly IEndpointsDeInfra _infra;
    private readonly string _virtualHost;
    private readonly Action<IServiceCollection>? _ajustes;
    private readonly IReadOnlyDictionary<string, string> _configuracao;

    // Falso quando o teste para o Mongo: o DropDatabase esperaria o timeout de selecao de servidor.
    public bool LimparBancoAoDescartar { get; set; } = true;

    public string NomeDoBanco { get; } = "execucao_t_" + Guid.NewGuid().ToString("N")[..10];

    public ApiComInfraFactory(
        IEndpointsDeInfra infra,
        string virtualHost,
        Action<IServiceCollection>? ajustes = null,
        IReadOnlyDictionary<string, string>? configuracao = null)
    {
        _infra = infra;
        _virtualHost = virtualHost;
        _ajustes = ajustes;
        _configuracao = configuracao ?? new Dictionary<string, string>();
    }

    public static async Task<ApiComInfraFactory> CriarAsync(
        IEndpointsDeInfra infra,
        Action<IServiceCollection>? ajustes = null,
        IReadOnlyDictionary<string, string>? configuracao = null) =>
        new(infra, await infra.CriarVirtualHostAsync(), ajustes, configuracao);

    public IMongoDatabase Banco => Services.GetRequiredService<IMongoDatabase>();

    public IMongoCollection<BsonDocument> Ordens => Banco.GetCollection<BsonDocument>("ordens_producao");

    protected override void ConfigurarAmbiente(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:MongoDb", _infra.StringDeConexaoDoMongo);
        builder.UseSetting("MongoDb:Database", NomeDoBanco);
        builder.UseSetting("RabbitMq:Host", _infra.HostDoRabbit);
        builder.UseSetting("RabbitMq:Port", _infra.PortaDoRabbit.ToString());
        builder.UseSetting("RabbitMq:VirtualHost", _virtualHost);
        builder.UseSetting("RabbitMq:Username", _infra.UsuarioDoRabbit);
        builder.UseSetting("RabbitMq:Password", _infra.SenhaDoRabbit);

        // Retentativas rapidas e outbox sondando a cada 100 ms: os testes esperam por condicoes e nao devem
        // ficar parados em intervalos de producao.
        builder.UseSetting("Mensageria:Retentativas", "3");
        builder.UseSetting("Mensageria:IntervaloInicialEmMs", "50");
        builder.UseSetting("Mensageria:IntervaloMaximoEmMs", "200");
        builder.UseSetting("Mensageria:IntervaloDoOutboxEmMs", "100");

        foreach (var (chave, valor) in _configuracao)
            builder.UseSetting(chave, valor);

        if (_ajustes is not null)
            builder.ConfigureServices(_ajustes);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && LimparBancoAoDescartar)
        {
            try
            {
                Banco.Client.DropDatabase(NomeDoBanco);
            }
            catch (Exception)
            {
                // Limpeza de melhor esforco: o container inteiro e removido ao final da execucao.
            }
        }

        base.Dispose(disposing);
    }
}
