namespace eShop.IntegrationEvents.Messaging;

/// <summary>
/// Abstraction du bus d'événements partagé : tout service qui publie des
/// événements d'intégration dépend de cette interface plutôt que d'une
/// implémentation concrète (RabbitMQ aujourd'hui).
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publie un événement d'intégration sur le bus avec la routing key fournie.
    /// </summary>
    Task PublishAsync<T>(T message, string routingKey) where T : IntegrationEvent;
}
