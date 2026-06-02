using Ordering.API.Domain.AggregatesModel.OrderAggregate;
using Ordering.API.Domain.Events;
using Xunit;

namespace Ordering.UnitTests;

// Tests unitaires sur les invariants stables de l'agrégat Order.
// On ne teste que le domaine pur (pas d'EF, pas de MediatR).
public class OrderAggregateTests
{
    // Fabrique un Order valide pour les scénarios de transition.
    private static Order CreateValidOrder()
    {
        var address = new Address("1 rue de la Paix", "Paris", "France", "75001");
        var items = new List<OrderItem>
        {
            new OrderItem(productId: 1, productName: "Produit A", unitPrice: 10m, units: 2)
        };
        return new Order("buyer-123", address, items);
    }

    // --- Constructeur : invariants ---

    [Fact]
    public void Constructeur_Leve_Si_BuyerId_Vide()
    {
        var address = new Address("rue", "ville", "pays", "0000");
        var items = new List<OrderItem>
        {
            new OrderItem(1, "Produit A", 10m, 1)
        };

        Assert.Throws<ArgumentException>(() => new Order(string.Empty, address, items));
    }

    [Fact]
    public void Constructeur_Leve_Si_Liste_Items_Vide()
    {
        var address = new Address("rue", "ville", "pays", "0000");
        var items = new List<OrderItem>();

        Assert.Throws<ArgumentException>(() => new Order("buyer-123", address, items));
    }

    [Fact]
    public void Constructeur_Order_Valide_A_Le_Statut_Submitted()
    {
        var order = CreateValidOrder();

        Assert.Equal(OrderStatus.Submitted, order.Status);
    }

    [Fact]
    public void Constructeur_Order_Valide_Leve_OrderPlacedDomainEvent()
    {
        var order = CreateValidOrder();

        Assert.Single(order.DomainEvents);
        var domainEvent = Assert.IsType<OrderPlacedDomainEvent>(order.DomainEvents.First());
        Assert.Same(order, domainEvent.Order);
    }

    // --- Transitions d'état ---

    [Fact]
    public void SetAwaitingValidation_Depuis_Submitted_Passe_En_AwaitingValidation()
    {
        var order = CreateValidOrder();

        order.SetAwaitingValidation();

        Assert.Equal(OrderStatus.AwaitingValidation, order.Status);
    }

    [Fact]
    public void SetAwaitingValidation_Leve_Si_Pas_Submitted()
    {
        var order = CreateValidOrder();
        order.SetAwaitingValidation(); // -> AwaitingValidation, n'est plus Submitted

        Assert.Throws<InvalidOperationException>(() => order.SetAwaitingValidation());
    }

    [Fact]
    public void SetStockConfirmed_Depuis_AwaitingValidation_Passe_En_StockConfirmed()
    {
        var order = CreateValidOrder();
        order.SetAwaitingValidation();

        order.SetStockConfirmed();

        Assert.Equal(OrderStatus.StockConfirmed, order.Status);
    }

    [Fact]
    public void SetStockConfirmed_Leve_Si_Pas_AwaitingValidation()
    {
        var order = CreateValidOrder(); // statut Submitted

        Assert.Throws<InvalidOperationException>(() => order.SetStockConfirmed());
    }

    [Fact]
    public void SetPaid_Depuis_StockConfirmed_Passe_En_Paid()
    {
        var order = CreateValidOrder();
        order.SetAwaitingValidation();
        order.SetStockConfirmed();

        order.SetPaid();

        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public void SetPaid_Leve_Si_Pas_StockConfirmed()
    {
        var order = CreateValidOrder();
        order.SetAwaitingValidation(); // statut AwaitingValidation, pas StockConfirmed

        Assert.Throws<InvalidOperationException>(() => order.SetPaid());
    }

    [Fact]
    public void Ship_Depuis_Paid_Passe_En_Shipped()
    {
        var order = CreateValidOrder();
        order.SetAwaitingValidation();
        order.SetStockConfirmed();
        order.SetPaid();

        order.Ship();

        Assert.Equal(OrderStatus.Shipped, order.Status);
    }

    [Fact]
    public void Ship_Leve_Si_Statut_Pas_Paid()
    {
        var order = CreateValidOrder();
        order.SetAwaitingValidation();
        order.SetStockConfirmed(); // StockConfirmed, pas encore Paid

        Assert.Throws<InvalidOperationException>(() => order.Ship());
    }

    [Fact]
    public void Cancel_Depuis_Submitted_Passe_En_Cancelled()
    {
        var order = CreateValidOrder();

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancel_Leve_Si_Order_Deja_Shipped()
    {
        var order = CreateValidOrder();
        order.SetAwaitingValidation();
        order.SetStockConfirmed();
        order.SetPaid();
        order.Ship(); // statut Shipped

        Assert.Throws<InvalidOperationException>(() => order.Cancel());
    }

    [Fact]
    public void Cancel_Depuis_StockConfirmed_Passe_En_Cancelled()
    {
        var order = CreateValidOrder();
        order.SetAwaitingValidation();
        order.SetStockConfirmed();

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void SetStockConfirmed_Leve_OrderStockConfirmedDomainEvent()
    {
        var order = CreateValidOrder();
        order.SetAwaitingValidation();

        order.SetStockConfirmed();

        Assert.Contains(order.DomainEvents, e => e is OrderStockConfirmedDomainEvent);
    }

    [Fact]
    public void SetPaid_Leve_OrderPaidDomainEvent()
    {
        var order = CreateValidOrder();
        order.SetAwaitingValidation();
        order.SetStockConfirmed();

        order.SetPaid();

        Assert.Contains(order.DomainEvents, e => e is OrderPaidDomainEvent);
    }

    // --- TotalPrice ---

    [Fact]
    public void TotalPrice_Est_La_Somme_Des_UnitPrice_Fois_Quantity()
    {
        var address = new Address("rue", "ville", "pays", "0000");
        var items = new List<OrderItem>
        {
            new OrderItem(1, "Produit A", unitPrice: 10m, units: 2), // 20
            new OrderItem(2, "Produit B", unitPrice: 5.5m, units: 3)  // 16.5
        };
        var order = new Order("buyer-123", address, items);

        Assert.Equal(36.5m, order.TotalPrice);
    }
}
