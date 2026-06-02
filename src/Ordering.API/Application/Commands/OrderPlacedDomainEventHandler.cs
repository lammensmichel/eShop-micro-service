using MediatR;
using eShop.IntegrationEvents.Events;
using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.Events;
using Ordering.API.Infrastructure.Outbox;

namespace Ordering.API.Application.Commands;

public class OrderPlacedDomainEventHandler : INotificationHandler<OrderPlacedDomainEvent>
{
    // Routing key d'émission de l'événement « commande soumise » (point d'entrée saga, Chantier B).
    public const string OrderSubmittedRoutingKey = "ordering-order-submitted";

    private readonly ILogger<OrderPlacedDomainEventHandler> _logger;
    private readonly IIntegrationEventLogService _eventLogService;

    public OrderPlacedDomainEventHandler(
        ILogger<OrderPlacedDomainEventHandler> logger,
        IIntegrationEventLogService eventLogService)
    {
        _logger = logger;
        _eventLogService = eventLogService;
    }

    public async Task Handle(OrderPlacedDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Order placed: {OrderId} for buyer {BuyerId} - Total: {Total}",
            notification.Order.Id,
            notification.Order.BuyerId,
            notification.Order.TotalPrice);

        // Traduction domain event -> integration event (pattern Outbox).
        // Ce handler s'exécute pendant le dispatch déclenché par SaveChangesAsync, lui-même
        // appelé À L'INTÉRIEUR de la transaction ouverte par RabbitMQConsumer.ProcessEventAsync.
        // SaveEventAsync ne fait qu'ajouter l'entrée au DbContext ; elle sera persistée par
        // le SaveChanges suivant de ProcessEventAsync, puis committée AVEC l'Order -> atomicité.
        var integrationEvent = new OrderStatusChangedToSubmittedIntegrationEvent
        {
            OrderId = notification.Order.Id,
            BuyerId = notification.Order.BuyerId,
            OrderStatus = notification.Order.Status.Name
        };

        await _eventLogService.SaveEventAsync(integrationEvent, OrderSubmittedRoutingKey);
    }
}
