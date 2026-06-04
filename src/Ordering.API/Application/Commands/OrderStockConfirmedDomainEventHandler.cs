using MediatR;
using eShop.IntegrationEvents.Events;
using Ordering.API.Domain.Events;
using Ordering.API.Infrastructure.Outbox;

namespace Ordering.API.Application.Commands;

// Traduit le domain event OrderStockConfirmed en event d'intégration sortant
// (pattern Outbox). S'exécute pendant le dispatch déclenché par SaveChangesAsync,
// lui-même appelé à l'intérieur de la transaction ouverte par le consumer : l'entrée
// outbox est donc persistée atomiquement avec le changement de statut de la commande.
public class OrderStockConfirmedDomainEventHandler : INotificationHandler<OrderStockConfirmedDomainEvent>
{
    // Routing key d'émission de l'événement « stock confirmé » (consommé par PaymentProcessor, étape 2).
    public const string OrderStockConfirmedRoutingKey = "ordering-order-stock-confirmed";

    private readonly ILogger<OrderStockConfirmedDomainEventHandler> _logger;
    private readonly IIntegrationEventLogService _eventLogService;

    public OrderStockConfirmedDomainEventHandler(
        ILogger<OrderStockConfirmedDomainEventHandler> logger,
        IIntegrationEventLogService eventLogService)
    {
        _logger = logger;
        _eventLogService = eventLogService;
    }

    public async Task Handle(OrderStockConfirmedDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Order stock confirmed: {OrderId} for buyer {BuyerId}",
            notification.Order.Id,
            notification.Order.BuyerId);

        // On ENRICHIT l'event sortant avec le montant à débiter (= total de la commande) et
        // le moyen de paiement conservé sur l'Order : c'est le PaymentProcessor qui réalise
        // la transaction, il lui faut donc de quoi débiter. ⚠️ Le PAN transite ici en clair
        // UNIQUEMENT par simulation pédagogique (cf. PaymentMethod.cs / contrat PCI-DSS).
        var integrationEvent = new OrderStockConfirmedIntegrationEvent
        {
            OrderId = notification.Order.Id,
            BuyerId = notification.Order.BuyerId,
            Amount = notification.Order.TotalPrice,
            CardNumber = notification.Order.PaymentMethod.CardNumber,
            CardHolderName = notification.Order.PaymentMethod.CardHolderName,
            CardExpiration = notification.Order.PaymentMethod.CardExpiration
        };

        await _eventLogService.SaveEventAsync(integrationEvent, OrderStockConfirmedRoutingKey);
    }
}
