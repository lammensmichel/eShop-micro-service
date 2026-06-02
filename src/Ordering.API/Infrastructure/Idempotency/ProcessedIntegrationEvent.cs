namespace Ordering.API.Infrastructure.Idempotency;

// Trace les événements d'intégration déjà traités (clé d'idempotence côté consommateur).
// Persistée dans la MÊME transaction que la commande pour garantir l'exactement-une-fois logique.
public class ProcessedIntegrationEvent
{
    public Guid EventId { get; set; }
    public DateTime ProcessedOn { get; set; }
}
