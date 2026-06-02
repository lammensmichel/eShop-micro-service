using System.Text.Json;
using eShop.IntegrationEvents.Messaging;

namespace Ordering.API.Infrastructure.Outbox;

// Entrée du journal d'événements d'intégration (table outbox).
// L'événement est sérialisé en JSON dans Content et persisté DANS LA MÊME transaction
// que le changement métier ; un background service le publiera ensuite de façon fiable
// (au moins une fois), garantissant qu'aucun événement n'est perdu entre le commit
// métier et la publication.
public class IntegrationEventLogEntry
{
    // Constructeur sans paramètre requis par EF Core pour la matérialisation.
    private IntegrationEventLogEntry() { }

    public IntegrationEventLogEntry(IntegrationEvent evt, string routingKey)
    {
        EventId = evt.Id;
        CreationTime = evt.CreationDate;
        // Nom de type assembly-qualifié court (sans version/culture/clé) : suffisant
        // pour redésérialiser tout en restant robuste aux changements de version.
        EventTypeName = evt.GetType().AssemblyQualifiedName!;
        Content = JsonSerializer.Serialize(evt, evt.GetType());
        RoutingKey = routingKey;
        State = IntegrationEventState.NotPublished;
        TimesSent = 0;
    }

    public Guid EventId { get; private set; }

    public string Content { get; private set; } = default!;

    // Nom de type assembly-qualifié permettant de retrouver le type concret au moment
    // de la republication (désérialisation depuis Content).
    public string EventTypeName { get; private set; } = default!;

    // Routing key d'origine, conservée pour que le publisher sache où republier.
    public string RoutingKey { get; private set; } = default!;

    public IntegrationEventState State { get; set; }

    public int TimesSent { get; set; }

    public DateTime CreationTime { get; private set; }

    // Désérialise le contenu JSON vers son type d'événement d'intégration concret.
    // Utilisé par le publisher de fond avant publication sur le bus.
    public IntegrationEvent DeserializeJsonContent()
    {
        var type = Type.GetType(EventTypeName)
            ?? throw new InvalidOperationException(
                $"Type d'événement introuvable : {EventTypeName}");

        return (IntegrationEvent)JsonSerializer.Deserialize(Content, type)!;
    }
}
