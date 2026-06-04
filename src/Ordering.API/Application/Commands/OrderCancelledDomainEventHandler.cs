using MediatR;
using Ordering.API.Domain.Events;

namespace Ordering.API.Application.Commands;

// HANDLER DE DOMAIN EVENT (INotificationHandler). MediatR l'appelle quand le DbContext
// dispatche les events accumulés après SaveChangesAsync (voir OrderingDbContext). Un même
// event peut avoir 0..n handlers (sémantique pub/sub) ; on les place dans Application/Commands
// car ils orchestrent les RÉACTIONS aux faits métier, hors du domaine lui-même.
//
// Celui-ci ne fait QUE journaliser : tous les domain events n'ont pas besoin de produire un
// effet externe. Comparer avec OrderPlacedDomainEventHandler / OrderStockConfirmedDomainEventHandler,
// qui eux traduisent le fait en integration event déposé dans l'outbox.
public class OrderCancelledDomainEventHandler : INotificationHandler<OrderCancelledDomainEvent>
{
    private readonly ILogger<OrderCancelledDomainEventHandler> _logger;

    public OrderCancelledDomainEventHandler(ILogger<OrderCancelledDomainEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(OrderCancelledDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Order cancelled: {OrderId} for buyer {BuyerId}",
            notification.Order.Id,
            notification.Order.BuyerId);

        return Task.CompletedTask;
    }
}
