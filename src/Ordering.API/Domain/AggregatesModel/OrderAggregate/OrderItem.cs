using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.AggregatesModel.OrderAggregate;

// Ligne de commande : ENTITÉ INTERNE de l'agrégat Order (elle a un Id, mais elle n'est
// PAS une racine d'agrégat — elle n'implémente pas IAggregateRoot). On ne la manipule
// donc jamais seule : pas de repository dédié, on la crée/lit toujours via Order.
// Setters privés + invariants dans le constructeur, comme pour toute entité du domaine.
public class OrderItem : Entity
{
    public int ProductId { get; private set; }
    public string ProductName { get; private set; }
    public decimal UnitPrice { get; private set; }
    public int Units { get; private set; }

    // Constructeur réservé à EF Core (voir Order).
    protected OrderItem()
    {
        ProductName = string.Empty;
    }

    public OrderItem(int productId, string productName, decimal unitPrice, int units)
    {
        // Invariants de la ligne : quantité et prix unitaire strictement positifs.
        if (units <= 0)
            throw new ArgumentException("Units must be greater than zero", nameof(units));
        if (unitPrice <= 0)
            throw new ArgumentException("Unit price must be greater than zero", nameof(unitPrice));

        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Units = units;
    }

    // Le total de la ligne est CALCULÉ, jamais stocké -> impossible de le désynchroniser.
    public decimal GetTotalPrice() => UnitPrice * Units;
}