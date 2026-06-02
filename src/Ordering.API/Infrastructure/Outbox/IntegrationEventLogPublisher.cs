using eShop.IntegrationEvents.Messaging;

namespace Ordering.API.Infrastructure.Outbox;

// Publisher de fond du pattern Outbox : à intervalle régulier, récupère les entrées
// en attente (NotPublished/Failed), les marque InProgress, publie sur le bus puis
// MarkAsPublished. En cas d'échec, MarkAsFailed (TimesSent++) -> retentée au cycle suivant.
// Garantie « au moins une fois » : tuer le process après le commit métier mais avant
// publication laisse l'entrée en attente ; au redémarrage, elle repart.
public class IntegrationEventLogPublisher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IntegrationEventLogPublisher> _logger;
    private readonly TimeSpan _pollingInterval;

    public IntegrationEventLogPublisher(
        IServiceScopeFactory scopeFactory,
        ILogger<IntegrationEventLogPublisher> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        // Intervalle de poll configurable (clé "Outbox:PollingIntervalSeconds"), défaut 5 s.
        var seconds = configuration.GetValue<int?>("Outbox:PollingIntervalSeconds") ?? 5;
        _pollingInterval = TimeSpan.FromSeconds(seconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingEventsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // On ne laisse jamais une exception tuer la boucle : on réessaiera.
                _logger.LogError(ex, "Erreur dans le cycle de publication de l'outbox");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }
    }

    private async Task PublishPendingEventsAsync(CancellationToken cancellationToken)
    {
        // Un scope DI par cycle : le DbContext (scoped) et le service du log sont neufs.
        using var scope = _scopeFactory.CreateScope();
        var logService = scope.ServiceProvider.GetRequiredService<IIntegrationEventLogService>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        var pending = await logService.RetrievePendingEventsAsync();
        if (pending.Count == 0) return;

        foreach (var entry in pending)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                await logService.MarkAsInProgressAsync(entry.EventId);

                var integrationEvent = entry.DeserializeJsonContent();
                await eventBus.PublishAsync(integrationEvent, entry.RoutingKey);

                await logService.MarkAsPublishedAsync(entry.EventId);

                _logger.LogInformation(
                    "Événement d'intégration {EventId} ({EventType}) publié",
                    entry.EventId, entry.EventTypeName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Échec de publication de l'événement d'intégration {EventId} ({EventType})",
                    entry.EventId, entry.EventTypeName);

                await logService.MarkAsFailedAsync(entry.EventId);
            }
        }
    }
}
