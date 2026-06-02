using MediatR;
using Ordering.API.Domain.Events;

namespace Ordering.API.Application.Commands;

// Le passage Paid ne produit aucun event d'intégration sortant pour la démo
// (la saga se poursuit localement par Ship()). On se contente de journaliser.
public class OrderPaidDomainEventHandler : INotificationHandler<OrderPaidDomainEvent>
{
    private readonly ILogger<OrderPaidDomainEventHandler> _logger;

    public OrderPaidDomainEventHandler(ILogger<OrderPaidDomainEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(OrderPaidDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Order paid: {OrderId} for buyer {BuyerId}",
            notification.Order.Id,
            notification.Order.BuyerId);

        return Task.CompletedTask;
    }
}
