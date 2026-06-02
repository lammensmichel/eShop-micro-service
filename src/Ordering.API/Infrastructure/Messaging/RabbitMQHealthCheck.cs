using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace Ordering.API.Infrastructure.Messaging;

// Health check RabbitMQ (point 12) : ouvre une connexion courte vers le broker
// pour vérifier sa disponibilité. Tout est asynchrone (aucun appel bloquant).
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
