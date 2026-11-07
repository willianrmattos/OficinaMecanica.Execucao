using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace OficinaMecanica.Execucao.Infrastructure.Persistence;

// Ping barato ao banco para a prontidao (/health/ready). O /health simples nao depende dele: um Atlas
// instavel nao deve reiniciar o pod, so tira-lo da rotacao.
public sealed class MongoHealthCheck(IMongoDatabase banco) : IHealthCheck
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(3);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limite.CancelAfter(Limite);

        try
        {
            await banco.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: limite.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MongoDB indisponível.", ex);
        }
    }
}
