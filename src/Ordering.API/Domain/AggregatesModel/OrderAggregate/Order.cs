using Ordering.API.Domain.Events;
using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.AggregatesModel.OrderAggregate;

// RACINE D'AGRÉGAT Order : le cœur DDD du service. Elle encapsule ses entités enfants
// (OrderItem) et ses value objects (Address, OrderStatus) et constitue la frontière de
// cohérence : toute modification passe par SES méthodes.
//
// Principe clé : tous les SETTERS sont `private`. On ne peut donc PAS mettre l'agrégat dans
// un état incohérent depuis l'extérieur (ex. forcer Status = Shipped sans paiement). Les
// INVARIANTS (règles toujours vraies) sont garantis à deux endroits :
//   1) le CONSTRUCTEUR -> un Order ne peut naître que valide (acheteur + au moins un article) ;
//   2) les TRANSITIONS d'état (Ship, Cancel, SetPaid...) -> chacune vérifie que le passage
//      est autorisé depuis l'état courant (machine à états), sinon elle lève une exception.
// Chaque transition réussie LÈVE un domain event décrivant le fait métier survenu ; ces
// events seront publiés après le SaveChanges (voir OrderingDbContext).
public class Order : Entity, IAggregateRoot
{
    public string BuyerId { get; private set; }
    public DateTime OrderDate { get; private set; }
    public Address Address { get; private set; }
    public OrderStatus Status { get; private set; }

    // Moyen de paiement conservé sur la commande (value object owned, cf. ⚠️ PCI-DSS dans
    // PaymentMethod.cs). Il est rachemine au PaymentProcessor au moment du stock-confirmed.
    public PaymentMethod PaymentMethod { get; private set; }

    // Référence de la transaction renvoyée par la passerelle de paiement (traçabilité).
    // Nullable : renseignée seulement une fois le paiement confirmé (SetPaid).
    public string? PaymentTransactionId { get; private set; }

    // Collection enfant ENCAPSULÉE : champ privé modifiable, exposé en lecture seule.
    // Impossible d'ajouter/retirer un article hors de l'agrégat -> la cohérence (et le
    // total) reste sous le contrôle d'Order.
    private readonly List<OrderItem> _orderItems = [];
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    // Total CALCULÉ à la volée depuis les lignes : aucune donnée dénormalisée à maintenir,
    // donc pas de risque de désynchronisation.
    public decimal TotalPrice => _orderItems.Sum(o => o.GetTotalPrice());

    // Constructeur sans paramètre RÉSERVÉ à EF Core (matérialisation depuis la base).
    // protected -> le code applicatif ne peut pas l'utiliser et doit passer par le
    // constructeur métier ci-dessous, qui seul applique les invariants.
    protected Order()
    {
        BuyerId = string.Empty;
        Address = null!;
        PaymentMethod = null!;
        Status = OrderStatus.Submitted;
    }

    // Constructeur MÉTIER : seule façon de créer une commande valide.
    public Order(string buyerId, Address address, List<OrderItem> items, PaymentMethod paymentMethod)
    {
        // Invariants vérifiés DÈS la création : un Order naît forcément cohérent.
        if (string.IsNullOrEmpty(buyerId))
            throw new ArgumentException("BuyerId cannot be empty", nameof(buyerId));
        if (items.Count == 0)
            throw new ArgumentException("Order must have at least one item", nameof(items));
        // Invariant : une commande conserve toujours le moyen de paiement choisi au checkout
        // (on en aura besoin pour débiter au moment du stock-confirmed).
        if (paymentMethod is null)
            throw new ArgumentNullException(nameof(paymentMethod));

        BuyerId = buyerId;
        Address = address;
        PaymentMethod = paymentMethod;
        OrderDate = DateTime.UtcNow;
        Status = OrderStatus.Submitted; // état initial de la machine à états
        _orderItems.AddRange(items);

        // Fait métier « commande passée » : levé ici, publié après persistance.
        // Son handler traduira l'event en integration event (outbox) pour la saga.
        AddDomainEvent(new OrderPlacedDomainEvent(this));
    }

    // --- TRANSITIONS DE LA MACHINE À ÉTATS ---
    // Cycle nominal : Submitted -> AwaitingValidation -> StockConfirmed -> Paid -> Shipped.
    // Cancel est possible depuis tout état SAUF Shipped. Chaque méthode refuse les
    // transitions illégales (garde-fou) et lève le domain event correspondant.

    // AwaitingValidation -> StockConfirmed.
    public void SetStockConfirmed()
    {
        if (Status != OrderStatus.AwaitingValidation)
            throw new InvalidOperationException("Order must be awaiting validation first");

        Status = OrderStatus.StockConfirmed;

        AddDomainEvent(new OrderStockConfirmedDomainEvent(this));
    }

    // StockConfirmed -> Paid. transactionId est OPTIONNEL : c'est la référence du paiement
    // renvoyée par la passerelle (le « reçu »), qu'on conserve pour la traçabilité. Optionnel
    // pour ne pas casser les appels existants ; renseigné depuis l'event « paiement réussi ».
    public void SetPaid(string? transactionId = null)
    {
        if (Status != OrderStatus.StockConfirmed)
            throw new InvalidOperationException("Order stock must be confirmed first");

        PaymentTransactionId = transactionId;
        Status = OrderStatus.Paid;

        AddDomainEvent(new OrderPaidDomainEvent(this));
    }

    // Paid -> Shipped. État terminal du parcours nominal.
    public void Ship()
    {
        if (Status != OrderStatus.Paid)
            throw new InvalidOperationException("Cannot ship an order that is not paid");

        Status = OrderStatus.Shipped;

        AddDomainEvent(new OrderShippedDomainEvent(this));
    }

    // Annulation : autorisée depuis n'importe quel état SAUF Shipped (on ne « dé-livre » pas).
    public void Cancel()
    {
        if (Status == OrderStatus.Shipped)
            throw new InvalidOperationException("Cannot cancel a shipped order");

        Status = OrderStatus.Cancelled;

        AddDomainEvent(new OrderCancelledDomainEvent(this));
    }

    // Submitted -> AwaitingValidation. Seule transition qui ne lève PAS de domain event
    // (aucune réaction métier nécessaire à ce stade dans cette implémentation).
    public void SetAwaitingValidation()
    {
        if (Status != OrderStatus.Submitted)
            throw new InvalidOperationException("Order must be submitted first");

        Status = OrderStatus.AwaitingValidation;
    }
}