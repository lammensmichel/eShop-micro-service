using eShop.IntegrationEvents.Messaging;

namespace eShop.IntegrationEvents.Events;

// =============================================================================
// FICHIER : BasketCheckoutEvent.cs
// RÔLE    : le PREMIER événement d'intégration de la chaîne. Il démarre tout le
//           processus de commande inter-services.
// CONCEPT : ÉVÉNEMENT AUTO-SUFFISANT + CHORÉGRAPHIE DE SAGA.
//
//   - "Auto-suffisant" : l'événement transporte TOUT ce dont le destinataire a
//     besoin (acheteur, adresse, carte, lignes, total). Ordering.API n'a PAS à
//     rappeler Basket.API pour compléter l'info. C'est essentiel en microservices :
//     on évite les couplages synchrones et les pannes en cascade.
//
//   - "Saga chorégraphiée" : la création d'une commande implique plusieurs services
//     (Ordering, OrderProcessor, PaymentProcessor). Plutôt qu'un chef d'orchestre
//     central, chaque service RÉAGIT à un événement et en PUBLIE un autre. La
//     logique est répartie ("chorégraphie") au lieu d'être centralisée
//     ("orchestration"). Les fichiers Events/*.cs sont les maillons de cette danse :
//
//   Basket.API ─BasketCheckoutEvent("basket-checkout")─▶ Ordering.API
//      (crée la commande, statut Submitted)
//        └─OrderStatusChangedToSubmittedIntegrationEvent─▶ OrderProcessor
//             (attend la grace period)
//        └─GracePeriodConfirmedIntegrationEvent("ordering-grace-period-confirmed")─▶ Ordering.API
//             (AwaitingValidation puis confirme le stock)
//        └─OrderStockConfirmedIntegrationEvent("ordering-order-stock-confirmed")─▶ PaymentProcessor
//             (simule le paiement)
//        ├─OrderPaymentSucceededIntegrationEvent("payment-succeeded")─▶ Ordering.API (Paid → Shipped)
//        └─OrderPaymentFailedIntegrationEvent("payment-failed")────────▶ Ordering.API (Cancelled)
//
// PLACE DANS LE FLUX : émis par Basket.API/Apis/BasketApi.cs (POST /checkout) via
//   IEventBus.PublishAsync(..., "basket-checkout"). Consommé par
//   Ordering.API/Infrastructure/Messaging/RabbitMQConsumer.cs, qui le traduit en
//   CreateOrderCommand (MediatR) pour bâtir l'agrégat Order.
//
// À LIRE :
//   - AVANT : IntegrationEvent.cs (la base), BasketApi.cs (l'émetteur).
//   - APRÈS : les autres Events/*.cs (la suite de la saga).
//
// Id (hérité de IntegrationEvent) est généré à la publication et sert de clé
// d'idempotence côté consommateur. NB : BasketApi régénère un Id à CHAQUE appel de
// checkout ; il ne protège donc PAS contre un double-clic utilisateur, seulement
// contre une redélivraison du MÊME message par le broker.
public record BasketCheckoutEvent : IntegrationEvent
{
    public required string BuyerId { get; init; }
    public required string City { get; init; }
    public required string Street { get; init; }
    public required string Country { get; init; }
    public required string ZipCode { get; init; }
    public required string CardNumber { get; init; }
    public required string CardHolderName { get; init; }
    public DateTime CardExpiration { get; init; }
    public decimal Total { get; init; }
    public List<BasketCheckoutItem> Items { get; init; } = [];
}

// Ligne de commande embarquée dans l'événement : une copie figée de chaque
// BasketItem (prix unitaire compris). On envoie une COPIE, pas une référence au
// panier : l'événement est un instantané du panier au moment du checkout, immuable
// et indépendant de l'évolution ultérieure du panier ou du catalogue.
public record BasketCheckoutItem
{
    public int ProductId { get; init; }
    public required string ProductName { get; init; }
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
}
