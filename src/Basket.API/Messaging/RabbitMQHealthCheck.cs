using eShop.IntegrationEvents.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Basket.API.Messaging;

// =============================================================================
// FICHIER : RabbitMQHealthCheck.cs
// RÔLE    : sonde de santé ("health check") signalant si RabbitMQ est joignable.
// CONCEPT : HEALTH CHECK + RÉUTILISATION DE LA CONNEXION PARTAGÉE.
//
//   Un "health check" est un test léger qu'expose le service (via /health, mappé
//   par MapDefaultEndpoints) pour qu'un orchestrateur (Aspire, Kubernetes...) sache
//   si la dépendance est OK. Tagué "ready" : il participe à la sonde de
//   "readiness" (le service est-il prêt à recevoir du trafic ?).
//
//   Astuce d'implémentation : on N'ouvre PAS une nouvelle connexion juste pour
//   tester. On réinjecte le RabbitMQPublisher (le TYPE CONCRET, résolvable grâce
//   au double enregistrement d'EventBusExtensions) et on lui demande de vérifier
//   sa connexion partagée + ouvrir un channel. On teste donc la connexion REELLE
//   utilisée en production, sans en créer une parasite.
//
// À LIRE : après RabbitMQPublisher.CheckConnectionAsync et EventBusExtensions.cs ;
//   enregistré dans Program.cs (AddHealthChecks().AddCheck<RabbitMQHealthCheck>).
// =============================================================================

/// <summary>
/// Health check RabbitMQ (point 12) : réutilise la connexion partagée du
/// <see cref="RabbitMQPublisher"/> (résolu depuis la lib partagée) et tente
/// d'ouvrir un channel. Aucun appel bloquant : tout est asynchrone.
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
