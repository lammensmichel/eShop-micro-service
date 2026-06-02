using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Basket.API.Messaging;

/// <summary>
/// Health check RabbitMQ (point 12) : réutilise la connexion partagée du
/// <see cref="RabbitMQPublisher"/> et tente d'ouvrir un channel. Aucun appel
/// bloquant : tout est asynchrone.
/// </summary>
public class RabbitMQHealthCheck : IHealthCheck
{
    private readonly RabbitMQPublisher _publisher;

    public RabbitMQHealthCheck(RabbitMQPublisher publisher)
    {
        _publisher = publisher;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _publisher.CheckConnectionAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Connexion RabbitMQ indisponible.", ex);
        }
    }
}
