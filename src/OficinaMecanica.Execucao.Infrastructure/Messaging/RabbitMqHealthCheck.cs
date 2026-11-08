using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace OficinaMecanica.Execucao.Infrastructure.Messaging;

// Abre (e fecha) uma conexao AMQP de verdade, com limite curto. Optei por nao depender so do check do bus do
// MassTransit: ele enxerga o estado interno do bus, que demora a refletir a queda do broker.
//
// O broker fora responde Degraded, e nao Unhealthy: com o outbox transacional o servico continua aceitando
// escrita (a mensagem espera no banco), entao tirar o pod de rotacao por causa do broker faria uma queda do
// RabbitMQ virar indisponibilidade total. Degraded mantem o /health/ready em 200 e aparece no corpo; o alerta
// operacional fica por metrica (outbox_mensagens_pendentes).
public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(3);

    private readonly IOptions<RabbitMqOptions> _opcoes;
    private readonly Func<ConnectionFactory, CancellationToken, Task<IConnection>> _conectar;

    public RabbitMqHealthCheck(IOptions<RabbitMqOptions> opcoes)
        : this(opcoes, (fabrica, token) => fabrica.CreateConnectionAsync(token))
    {
    }

    // Ponto de troca para teste: permite simular uma conexao que so completa depois do limite.
    internal RabbitMqHealthCheck(IOptions<RabbitMqOptions> opcoes, Func<ConnectionFactory, CancellationToken, Task<IConnection>> conectar)
    {
        _opcoes = opcoes;
        _conectar = conectar;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var rabbit = _opcoes.Value;
        var fabrica = new ConnectionFactory
        {
            HostName = rabbit.Host,
            Port = rabbit.Port,
            VirtualHost = rabbit.VirtualHost,
            UserName = rabbit.Username,
            Password = rabbit.Password,
            ClientProvidedName = "OficinaMecanica.Execucao.health",
            RequestedConnectionTimeout = Limite
        };

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limite.CancelAfter(Limite);

        var tentativa = _conectar(fabrica, limite.Token);

        try
        {
            // O WaitAsync garante o limite mesmo quando o cliente nao o respeita: com o broker congelado (TCP aceito,
            // handshake sem resposta) o cancelamento nao interrompe a espera e a desistencia do cliente leva ~8 s
            // (medido), o que estouraria o timeout de 5 s da readiness do Kubernetes.
            await using var conexao = await tentativa.WaitAsync(Limite, cancellationToken);
            return HealthCheckResult.Healthy("Conexão AMQP com o RabbitMQ estabelecida.");
        }
        catch (Exception ex)
        {
            // A tentativa e entregue ao fechamento nos dois desfechos (limite estourado e cancelamento do chamador): um
            // broker congelado que completar o handshake depois nao pode deixar uma conexao aberta.
            _ = FecharSeConectarDepoisAsync(tentativa);

            // Se quem cancelou foi o chamador (requisicao abortada), a excecao sobe: virar Degraded gravaria uma
            // medicao falsa no HealthCheckComCache. So o limite proprio de 3 s, ou uma falha da conexao, e Degraded.
            if (cancellationToken.IsCancellationRequested)
                throw;

            return HealthCheckResult.Degraded("Não foi possível conectar ao RabbitMQ; as mensagens ficam no outbox até o broker voltar.", ex);
        }
    }

    // A tentativa abandonada (pelo limite ou pelo cancelamento do chamador) continua em segundo plano: se ainda conectar,
    // fecho a conexao para nao vazar e, se falhar, observo a excecao para ela nao ficar sem tratamento.
    private static async Task FecharSeConectarDepoisAsync(Task<IConnection> tentativa)
    {
        try
        {
            await using var conexao = await tentativa;
        }
        catch (Exception)
        {
            // Falhou ou foi cancelada: nao ha conexao a fechar.
        }
    }
}
