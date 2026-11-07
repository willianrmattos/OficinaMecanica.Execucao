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
public sealed class RabbitMqHealthCheck(IOptions<RabbitMqOptions> opcoes) : IHealthCheck
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(3);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var rabbit = opcoes.Value;
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

        try
        {
            await using var conexao = await fabrica.CreateConnectionAsync(limite.Token);
            return HealthCheckResult.Healthy("Conexão AMQP com o RabbitMQ estabelecida.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded("Não foi possível conectar ao RabbitMQ; as mensagens ficam no outbox até o broker voltar.", ex);
        }
    }
}
