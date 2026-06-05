using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace OrderProcessor;

// ============================================================================
// WorkerHealthCheck — sonde de READINESS applicative du worker OrderProcessor.
// ----------------------------------------------------------------------------
// CIBLE K8s : la readinessProbe interroge /health (exposé par MapDefaultEndpoints).
// Comme ce worker n'a PAS d'API HTTP métier, « être prêt » signifie ici :
//   (a) RabbitMQ est joignable (on ouvre une connexion + un channel courts) — même
//       pattern que le RabbitMQHealthCheck des autres services ; ET
//   (b) le worker a effectivement DÉMARRÉ sa consommation (ConsumerState.IsConsuming),
//       c.-à-d. BasicConsumeAsync est passé. Sans ce drapeau, un process vivant mais
//       pas encore abonné (ou dont l'abonnement a chuté) serait considéré « prêt » à
//       tort. Tagué "ready" -> compte pour la readiness, pas pour la liveness (/alive).
//
// On garde l'ouverture RabbitMQ COURTE et asynchrone (non bloquante) : la sonde ne
// doit pas peser sur le broker ni bloquer le thread.
public sealed class WorkerHealthCheck : IHealthCheck
{
    private readonly string _connectionString;
    private readonly ConsumerState _state;

    public WorkerHealthCheck(IConfiguration configuration, ConsumerState state)
    {
        _connectionString = configuration.GetConnectionString("rabbitmq")!;
        _state = state;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // (b) Le worker consomme-t-il déjà ? Si non, pas encore prêt (mais bien vivant).
        if (!_state.IsConsuming)
        {
            return HealthCheckResult.Unhealthy("Consommateur RabbitMQ pas encore démarré.");
        }

        // (a) RabbitMQ est-il joignable ? On ouvre une connexion + un channel courts.
        try
        {
            var factory = new ConnectionFactory { Uri = new Uri(_connectionString) };
            await using var connection = await factory.CreateConnectionAsync(cancellationToken);
            if (!connection.IsOpen)
            {
                return HealthCheckResult.Unhealthy("Connexion RabbitMQ fermée.");
            }

            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            return channel.IsOpen
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Channel RabbitMQ indisponible.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Connexion RabbitMQ indisponible.", ex);
        }
    }
}
