namespace eShop.IntegrationEvents.Events;

public record BasketCheckoutEvent
{
    // Identifiant unique de l'événement, généré à la publication.
    // Sert de clé d'idempotence côté consommateur (Ordering.API) pour éviter
    // de créer une commande en double si le message est redélivré.
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string BuyerId { get; init; }
    public required string City { get; init; }
    public required string Street { get; init; }
    public required string Country { get; init; }
    public required string ZipCode { get; init; }
    public required string CardNumber { get; init; }
    public required string CardHolderName { get; init; }
    public DateTime CardExpiration { get; init; }
    public decimal Total { get; init; }
    public List<BasketCheckoutItem> Items { get; init; } = [];
}

public record BasketCheckoutItem
{
    public int ProductId { get; init; }
    public required string ProductName { get; init; }
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
}