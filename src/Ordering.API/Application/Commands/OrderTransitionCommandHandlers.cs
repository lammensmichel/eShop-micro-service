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
