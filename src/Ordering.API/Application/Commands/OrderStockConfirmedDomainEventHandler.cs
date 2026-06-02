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

        var integrationEvent = new OrderStockConfirmedIntegrationEvent
        {
            OrderId = notification.Order.Id,
            BuyerId = notification.Order.BuyerId
        };

        await _eventLogService.SaveEventAsync(integrationEvent, OrderStockConfirmedRoutingKey);
    }
}
