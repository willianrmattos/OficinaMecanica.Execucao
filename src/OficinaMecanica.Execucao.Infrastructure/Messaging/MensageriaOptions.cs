using Microsoft.Extensions.Configuration;

namespace OficinaMecanica.Execucao.Infrastructure.Messaging;

public enum TransporteDeMensagens
{
    RabbitMq = 0,

    // Transporte em memoria do MassTransit: so para testes sem broker.
    EmMemoria = 1
}

public enum OutboxDeMensagens
{
    // Outbox e inbox transacionais do MassTransit.MongoDb (exige replica set).
    MongoDb = 0,

    // Sem outbox: publicacao direta. So para testes que trocam a persistencia.
    Nenhum = 1
}

public sealed class RabbitMqOptions
{
    public const string Secao = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public ushort Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class MensageriaOptions
{
    public const string Secao = "Mensageria";

    public TransporteDeMensagens Transporte { get; set; } = TransporteDeMensagens.RabbitMq;

    public OutboxDeMensagens Outbox { get; set; } = OutboxDeMensagens.MongoDb;

    // Retentativas do consumer: backoff exponencial entre IntervaloInicialEmMs e IntervaloMaximoEmMs.
    public int Retentativas { get; set; } = 5;
    public int IntervaloInicialEmMs { get; set; } = 200;
    public int IntervaloMaximoEmMs { get; set; } = 30_000;

    // Frequencia com que o servico de entrega le o outbox: cada leitura custa operacoes no banco, e o M0 do
    // Atlas estrangula em 100 operacoes por segundo.
    public int IntervaloDoOutboxEmMs { get; set; } = 1_000;

    public int JanelaDeDeduplicacaoEmMinutos { get; set; } = 30;

    // Os dois switches abaixo sao fail-closed: o binder aceita qualquer numero para o enum (Transporte=9), e um
    // if/else com "senao, em memoria" transformaria um valor desconhecido em transporte em memoria e sem outbox, que
    // perde mensagem em silencio. Valor indefinido lanca, em qualquer ambiente.
    public bool UsaRabbitMq => Transporte switch
    {
        TransporteDeMensagens.RabbitMq => true,
        TransporteDeMensagens.EmMemoria => false,
        _ => throw ValorIndefinido("Transporte", Transporte)
    };

    public bool UsaOutboxDoMongo => Outbox switch
    {
        OutboxDeMensagens.MongoDb => true,
        OutboxDeMensagens.Nenhum => false,
        _ => throw ValorIndefinido("Outbox", Outbox)
    };

    public static InvalidOperationException ValorIndefinido<TEnum>(string chave, TEnum valor) where TEnum : struct, Enum =>
        new($"Mensageria:{chave}={Convert.ToInt64(valor)} não é um valor válido (use {string.Join(" ou ", Enum.GetNames<TEnum>())}).");

    public static MensageriaOptions Carregar(IConfiguration configuracao)
    {
        var opcoes = configuracao.GetSection(Secao).Get<MensageriaOptions>() ?? new MensageriaOptions();

        // Falha ja no registro dos servicos, antes de qualquer conexao.
        _ = opcoes.UsaRabbitMq;
        _ = opcoes.UsaOutboxDoMongo;

        return opcoes;
    }

    public static RabbitMqOptions CarregarRabbitMq(IConfiguration configuracao) =>
        configuracao.GetSection(RabbitMqOptions.Secao).Get<RabbitMqOptions>() ?? new RabbitMqOptions();

    // Exponencial por duplicacao do intervalo inicial, limitado ao maximo.
    public IReadOnlyList<TimeSpan> IntervalosDeRetentativa()
    {
        var intervalos = new List<TimeSpan>(Retentativas);
        var atual = (double)IntervaloInicialEmMs;

        for (var i = 0; i < Retentativas; i++)
        {
            intervalos.Add(TimeSpan.FromMilliseconds(Math.Min(atual, IntervaloMaximoEmMs)));
            atual *= 2;
        }

        return intervalos;
    }
}
