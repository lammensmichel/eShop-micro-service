using MediatR;
using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Application.Commands;

// Handlers des transitions du cycle de vie de la commande (point 9).
// La persistance déclenche le dispatch automatique des domain events
// via l'override de SaveChangesAsync du DbContext.

public class SetAwaitingValidationCommandHandler : IRequestHandler<SetAwaitingValidationCommand, Unit>
{
    private readonly IRepository<Order> _orderRepository;

    public SetAwaitingValidationCommandHandler(IRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Unit> Handle(SetAwaitingValidationCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetAsync(request.OrderId)
            ?? throw new KeyNotFoundException($"Order {request.OrderId} not found");

        // Contrôle de propriété (anti-IDOR) : la commande doit appartenir à l'appelant.
        if (order.BuyerId != request.BuyerId)
            throw new KeyNotFoundException($"Order {request.OrderId} not found");

        order.SetAwaitingValidation();
        _orderRepository.Update(order);
        await _orderRepository.SaveChangesAsync();

        return Unit.Value;
    }
}

public class ShipOrderCommandHandler : IRequestHandler<ShipOrderCommand, Unit>
{
    private readonly IRepository<Order> _orderRepository;

    public ShipOrderCommandHandler(IRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Unit> Handle(ShipOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetAsync(request.OrderId)
            ?? throw new KeyNotFoundException($"Order {request.OrderId} not found");

        // Contrôle de propriété (anti-IDOR) : la commande doit appartenir à l'appelant.
        if (order.BuyerId != request.BuyerId)
            throw new KeyNotFoundException($"Order {request.OrderId} not found");

        order.Ship();
        _orderRepository.Update(order);
        await _orderRepository.SaveChangesAsync();

        return Unit.Value;
    }
}

public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, Unit>
{
    private readonly IRepository<Order> _orderRepository;

    public CancelOrderCommandHandler(IRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Unit> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetAsync(request.OrderId)
            ?? throw new KeyNotFoundException($"Order {request.OrderId} not found");

        // Contrôle de propriété (anti-IDOR) : la commande doit appartenir à l'appelant.
        if (order.BuyerId != request.BuyerId)
            throw new KeyNotFoundException($"Order {request.OrderId} not found");

        order.Cancel();
        _orderRepository.Update(order);
        await _orderRepository.SaveChangesAsync();

        return Unit.Value;
    }
}

// --- Handlers des transitions pilotées par la saga (Chantier B) ---

// GracePeriodConfirmed -> AwaitingValidation puis confirmation du stock (simplifiée).
// SetStockConfirmed() lève OrderStockConfirmedDomainEvent dont le handler enfile
// l'event d'intégration sortant dans l'outbox (toujours dans la transaction du consumer).
public class ConfirmGracePeriodCommandHandler : IRequestHandler<ConfirmGracePeriodCommand, Unit>
{
    private readonly IRepository<Order> _orderRepository;

    public ConfirmGracePeriodCommandHandler(IRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Unit> Handle(ConfirmGracePeriodCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetAsync(request.OrderId)
            ?? throw new KeyNotFoundException($"Order {request.OrderId} not found");

        order.SetAwaitingValidation();
        // Validation de stock simplifiée : auto-confirmée, sans appel réel à Catalog.
        order.SetStockConfirmed();
        _orderRepository.Update(order);
        await _orderRepository.SaveChangesAsync();

        return Unit.Value;
    }
}

// OrderPaymentSucceeded -> Paid puis Shipped.
public class ConfirmOrderPaymentCommandHandler : IRequestHandler<ConfirmOrderPaymentCommand, Unit>
{
    private readonly IRepository<Order> _orderRepository;

    public ConfirmOrderPaymentCommandHandler(IRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Unit> Handle(ConfirmOrderPaymentCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetAsync(request.OrderId)
            ?? throw new KeyNotFoundException($"Order {request.OrderId} not found");

        order.SetPaid();
        order.Ship();
        _orderRepository.Update(order);
        await _orderRepository.SaveChangesAsync();

        return Unit.Value;
    }
}

// OrderPaymentFailed -> Cancelled.
public class CancelOrderPaymentCommandHandler : IRequestHandler<CancelOrderPaymentCommand, Unit>
{
    private readonly IRepository<Order> _orderRepository;

    public CancelOrderPaymentCommandHandler(IRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Unit> Handle(CancelOrderPaymentCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetAsync(request.OrderId)
            ?? throw new KeyNotFoundException($"Order {request.OrderId} not found");

        order.Cancel();
        _orderRepository.Update(order);
        await _orderRepository.SaveChangesAsync();

        return Unit.Value;
    }
}
