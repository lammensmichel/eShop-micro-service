using MediatR;
using Ordering.API.Domain.Events;

namespace Ordering.API.Application.Commands;

// HANDLER DE DOMAIN EVENT (réaction à un fait métier, dispatché par MediatR après
// SaveChangesAsync — voir OrderingDbContext). Pour le patron général, voir
// OrderCancelledDomainEventHandler.
//
// Le passage Paid ne produit aucun integration event sortant ici : la saga se poursuit
// LOCALEMENT, car le handler de commande ConfirmOrderPayment enchaîne directement SetPaid()
// puis Ship() dans la même transaction. On se contente donc de journaliser.
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
