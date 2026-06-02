using Ordering.API.Domain.Events;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.AggregatesModel.OrderAggregate;

public class Order : Entity, IAggregateRoot
{
    public string BuyerId { get; private set; }
    public DateTime OrderDate { get; private set; }
    public Address Address { get; private set; }
    public OrderStatus Status { get; private set; }

    private readonly List<OrderItem> _orderItems = [];
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    public decimal TotalPrice => _orderItems.Sum(o => o.GetTotalPrice());

    protected Order()
    {
        BuyerId = string.Empty;
        Address = null!;
        Status = OrderStatus.Submitted;
    }

    public Order(string buyerId, Address address, List<OrderItem> items)
    {
        if (string.IsNullOrEmpty(buyerId))
            throw new ArgumentException("BuyerId cannot be empty", nameof(buyerId));
        if (items.Count == 0)
            throw new ArgumentException("Order must have at least one item", nameof(items));

        BuyerId = buyerId;
        Address = address;
        OrderDate = DateTime.UtcNow;
        Status = OrderStatus.Submitted;
        _orderItems.AddRange(items);

        AddDomainEvent(new OrderPlacedDomainEvent(this));
    }

    public void SetStockConfirmed()
    {
        if (Status != OrderStatus.AwaitingValidation)
            throw new InvalidOperationException("Order must be awaiting validation first");

        Status = OrderStatus.StockConfirmed;

        AddDomainEvent(new OrderStockConfirmedDomainEvent(this));
    }

    public void SetPaid()
    {
        if (Status != OrderStatus.StockConfirmed)
            throw new InvalidOperationException("Order stock must be confirmed first");

        Status = OrderStatus.Paid;

        AddDomainEvent(new OrderPaidDomainEvent(this));
    }

    public void Ship()
    {
        if (Status != OrderStatus.Paid)
            throw new InvalidOperationException("Cannot ship an order that is not paid");

        Status = OrderStatus.Shipped;

        AddDomainEvent(new OrderShippedDomainEvent(this));
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Shipped)
            throw new InvalidOperationException("Cannot cancel a shipped order");

        Status = OrderStatus.Cancelled;

        AddDomainEvent(new OrderCancelledDomainEvent(this));
    }

    public void SetAwaitingValidation()
    {
        if (Status != OrderStatus.Submitted)
            throw new InvalidOperationException("Order must be submitted first");

        Status = OrderStatus.AwaitingValidation;
    }
}