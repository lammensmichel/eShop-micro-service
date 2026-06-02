using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.AggregatesModel.OrderAggregate;

public class OrderItem : Entity
{
    public int ProductId { get; private set; }
    public string ProductName { get; private set; }
    public decimal UnitPrice { get; private set; }
    public int Units { get; private set; }

    protected OrderItem() 
    { 
        ProductName = string.Empty;
    }

    public OrderItem(int productId, string productName, decimal unitPrice, int units)
    {
        if (units <= 0)
            throw new ArgumentException("Units must be greater than zero", nameof(units));
        if (unitPrice <= 0)
            throw new ArgumentException("Unit price must be greater than zero", nameof(unitPrice));

        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Units = units;
    }

    public decimal GetTotalPrice() => UnitPrice * Units;
}