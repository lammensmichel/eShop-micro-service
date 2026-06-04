using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace Ordering.API.Infrastructure.Messaging;

// HEALTH CHECK RabbitMQ : sonde de santé branchée sur l'infrastructure de health checks
// d'ASP.NET (voir Program.cs / AddHealthChecks). Elle ouvre une connexion courte vers le
// broker pour vérifier qu'il est joignable. Le tag "ready" la classe parmi les checks de
// « readiness » (le service est-il prêt à recevoir du trafic ?), exposés par MapDefaultEndpoints
// de ServiceDefaults et utilisés par Aspire/l'orchestrateur. Tout est asynchrone (non bloquant).
public class RabbitMQHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public RabbitMQHealthCheck(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("rabbitmq")!;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var factory = new ConnectionFactory { Uri = new Uri(_connectionString) };
            await using var connection = await factory.CreateConnectionAsync(cancellationToken);
            return connection.IsOpen
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Connexion RabbitMQ fermée.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Connexion RabbitMQ indisponible.", ex);
        }
    }
}
