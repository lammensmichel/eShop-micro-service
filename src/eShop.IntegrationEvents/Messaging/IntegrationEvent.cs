namespace eShop.IntegrationEvents.Messaging;

// =============================================================================
// FICHIER : IntegrationEvent.cs
// RÔLE    : classe de base (record abstrait) dont héritent TOUS les messages
//           échangés entre microservices via le bus. C'est la brique de base de
//           la couche "messagerie" du projet.
// CONCEPT : ÉVÉNEMENT D'INTÉGRATION.
//
//   - Un « événement d'intégration » est un fait métier qu'un service publie pour
//     que d'AUTRES services (dans d'autres processus, d'autres bases de données)
//     puissent réagir. Il franchit la frontière d'un service. Exemple :
//     "un panier a été validé" -> BasketCheckoutEvent.
//   - À NE PAS CONFONDRE avec un « domain event » (événement de domaine, cf.
//     Ordering.API/Domain). Un domain event reste DANS un service, en mémoire,
//     et exprime un fait à l'intérieur d'un agrégat (ex. OrderPlacedDomainEvent).
//     Un événement d'intégration, lui, est sérialisé en JSON et transite par le
//     réseau (RabbitMQ). Règle mnémotechnique :
//         domain event  = communication INTRA-service (in-process, MediatR)
//         integration event = communication INTER-services (réseau, bus RabbitMQ)
//
// PLACE DANS LE FLUX : c'est le "contrat" partagé. Le producteur (ex. Basket.API)
//   et le consommateur (ex. Ordering.API) référencent la même classe d'événement
//   issue de cette bibliothèque, ce qui garantit qu'ils parlent le même langage.
//
// À LIRE :
//   - APRÈS : Events/*.cs (les événements concrets qui héritent d'ici).
//   - AVANT : IEventBus.cs puis RabbitMQPublisher.cs (comment ces événements
//             voyagent réellement sur le réseau).
// =============================================================================

/// <summary>
/// Classe de base de tout événement d'intégration publié sur le bus partagé.
/// Porte un identifiant unique (<see cref="Id"/>) servant de clé d'idempotence
/// côté consommateur, ainsi que sa date de création (UTC).
/// </summary>
/// <remarks>
/// IDEMPOTENCE — pourquoi <see cref="Id"/> est essentiel : un bus de messages
/// fiable garantit une livraison "au moins une fois" (at-least-once), pas
/// "exactement une fois". Un même message PEUT donc être redélivré (ex. l'accusé
/// de réception du consommateur se perd, le broker rejoue le message). Le
/// consommateur stocke les Id déjà traités et ignore les doublons : traiter deux
/// fois le même Id ne doit produire aucun effet supplémentaire. C'est ça,
/// "être idempotent".
///
/// SÉRIALISATION — les valeurs par défaut (nouveau Guid / date courante) sont
/// initialisées dans le constructeur sans paramètre plutôt que dans des
/// initialiseurs de propriété, afin de ne pas interférer avec la désérialisation :
/// System.Text.Json appelle d'abord ce constructeur, puis réaffecte les propriétés
/// <c>init</c> présentes dans le JSON. L'identifiant émis à la publication est donc
/// bien préservé à la réception (sinon chaque réception regénérerait un Id et
/// casserait l'idempotence ci-dessus).
/// </remarks>
public abstract record IntegrationEvent
{
    protected IntegrationEvent()
    {
        Id = Guid.NewGuid();
        CreationDate = DateTime.UtcNow;
    }

    public Guid Id { get; init; }

    public DateTime CreationDate { get; init; }
}
