using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.AggregatesModel.OrderAggregate;

public class OrderStatus : ValueObject
{
    public static readonly OrderStatus Submitted = new(1, nameof(Submitted).ToLower());
    public static readonly OrderStatus AwaitingValidation = new(2, nameof(AwaitingValidation).ToLower());
    public static readonly OrderStatus Shipped = new(3, nameof(Shipped).ToLower());
    public static readonly OrderStatus Cancelled = new(4, nameof(Cancelled).ToLower());
    public static readonly OrderStatus StockConfirmed = new(5, nameof(StockConfirmed).ToLower());
    public static readonly OrderStatus Paid = new(6, nameof(Paid).ToLower());

    public int Id { get; }
    public string Name { get; }

    private OrderStatus(int id, string name)
    {
        Id = id;
        Name = name;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Id;
    }

    public override string ToString() => Name;
}