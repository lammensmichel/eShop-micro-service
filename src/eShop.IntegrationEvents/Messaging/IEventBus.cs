namespace eShop.IntegrationEvents.Messaging;

// =============================================================================
// FICHIER : IEventBus.cs
// RÔLE    : interface (le "port" en architecture hexagonale) du bus d'événements.
// CONCEPT : BUS D'ÉVÉNEMENTS + INVERSION DE DÉPENDANCE.
//
//   - Un « bus d'événements » (event bus) est le canal par lequel un service
//     publie ses événements d'intégration sans connaître QUI les recevra. Le
//     producteur ne référence aucun consommateur : il dépose un message, le bus
//     (RabbitMQ) se charge de l'acheminer. C'est ce qui DÉCOUPLE les microservices.
//
//   - Le code métier dépend de cette INTERFACE, pas de la classe concrète
//     RabbitMQPublisher. Avantages : on pourrait remplacer RabbitMQ par Azure
//     Service Bus / Kafka sans toucher aux endpoints ; et on peut fournir un faux
//     (mock) en test. C'est le "D" de SOLID (Dependency Inversion).
//
// PLACE DANS LE FLUX : Basket.API/Apis/BasketApi.cs reçoit un IEventBus par
//   injection et appelle PublishAsync(...) au checkout. L'implémentation réelle
//   est RabbitMQPublisher, branchée dans le conteneur DI par EventBusExtensions.
//
// À LIRE :
//   - AVANT : IntegrationEvent.cs (ce qui transite ici).
//   - APRÈS : RabbitMQPublisher.cs (l'implémentation concrète).
// =============================================================================

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
    /// <remarks>
    /// La « routing key » (clé de routage) est une étiquette texte attachée au
    /// message. Sur un exchange de type "direct" (cf. RabbitMQPublisher), RabbitMQ
    /// compare cette clé à celles des queues abonnées et délivre le message à la
    /// queue correspondante. Ex. : "basket-checkout" -> queue d'Ordering.API.
    /// C'est l'équivalent d'une "adresse de destination" logique.
    /// </remarks>
    Task PublishAsync<T>(T message, string routingKey) where T : IntegrationEvent;
}
