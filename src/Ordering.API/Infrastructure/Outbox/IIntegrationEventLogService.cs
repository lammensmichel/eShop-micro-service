using eShop.IntegrationEvents.Messaging;

namespace Ordering.API.Infrastructure.Outbox;

// Service du pattern Outbox : enregistre les événements d'intégration dans le journal
// (même transaction que le métier) et pilote leurs transitions d'état pour la publication.
public interface IIntegrationEventLogService
{
    // Ajoute une entrée NotPublished au DbContext. NE COMMITE PAS : l'entrée participe
    // à la transaction ambiante (celle ouverte par ProcessEventAsync), pour garantir
    // l'atomicité outbox + changement métier.
    Task SaveEventAsync(IntegrationEvent evt, string routingKey);

    // Récupère les entrées en attente de publication (NotPublished ou Failed), ordonnées
    // par date de création pour préserver l'ordre d'émission.
    Task<IReadOnlyList<IntegrationEventLogEntry>> RetrievePendingEventsAsync();

    Task MarkAsInProgressAsync(Guid eventId);
    Task MarkAsPublishedAsync(Guid eventId);
    Task MarkAsFailedAsync(Guid eventId);
}
