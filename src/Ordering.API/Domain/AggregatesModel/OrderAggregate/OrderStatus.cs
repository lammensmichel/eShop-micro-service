using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.AggregatesModel.OrderAggregate;

// Statut de commande modélisé en ENUMERATION CLASS (value object) plutôt qu'en enum C#.
// Avantages sur un simple enum : on peut attacher un comportement et des données (Id + Name),
// la valeur est fortement typée, et l'égalité par valeur est héritée de ValueObject.
// Les instances sont des SINGLETONS statiques readonly : il n'existe qu'un seul Submitted,
// un seul Paid, etc. — constructeur privé, donc personne ne peut en fabriquer d'autres.
// L'Id (entier stable) est ce qui est persisté en base (voir la conversion dans le DbContext).
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

    // Identité de valeur portée par l'Id seul : deux OrderStatus de même Id sont égaux.
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Id;
    }

    public override string ToString() => Name;
}