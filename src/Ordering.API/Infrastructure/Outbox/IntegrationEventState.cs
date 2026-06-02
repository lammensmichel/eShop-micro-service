namespace Ordering.API.Infrastructure.Outbox;

// États d'une entrée du journal d'événements d'intégration (pattern Outbox).
public enum IntegrationEventState
{
    // Persistée dans la même transaction que le changement métier, pas encore publiée.
    NotPublished = 0,
    // Sélectionnée par le publisher de fond, publication en cours.
    InProgress = 1,
    // Publiée avec succès sur le bus.
    Published = 2,
    // Échec de publication : sera retentée par le publisher (voir TimesSent).
    Failed = 3
}
