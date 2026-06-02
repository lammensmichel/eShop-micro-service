using MediatR;
using Ordering.API.Domain.Events;

namespace Ordering.API.Application.Commands;

public class OrderPlacedDomainEventHandler : INotificationHandler<OrderPlacedDomainEvent>
{
    private readonly ILogger<OrderPlacedDomainEventHandler> _logger;

    public OrderPlacedDomainEventHandler(ILogger<OrderPlacedDomainEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(OrderPlacedDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Order placed: {OrderId} for buyer {BuyerId} - Total: {Total}",
            notification.Order.Id,
            notification.Order.BuyerId,
            notification.Order.TotalPrice);

        return Task.CompletedTask;
    }
}