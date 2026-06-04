using MediatR;
using Ordering.API.Domain.Events;

namespace Ordering.API.Application.Commands;

// HANDLER DE DOMAIN EVENT (réaction à un fait métier, dispatché par MediatR après
// SaveChangesAsync — voir OrderingDbContext). Pour le patron général, voir
// OrderCancelledDomainEventHandler.
//
// Shipped est l'état terminal du parcours nominal : aucune étape ne suit, ce handler se
// contente donc de journaliser le fait.
public class OrderShippedDomainEventHandler : INotificationHandler<OrderShippedDomainEvent>
{
    private readonly ILogger<OrderShippedDomainEventHandler> _logger;

    public OrderShippedDomainEventHandler(ILogger<OrderShippedDomainEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(OrderShippedDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Order shipped: {OrderId} for buyer {BuyerId}",
            notification.Order.Id,
            notification.Order.BuyerId);

        return Task.CompletedTask;
    }
}
