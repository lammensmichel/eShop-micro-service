using MediatR;

namespace Ordering.API.Application.Queries;

// Côté QUERY du CQRS : une requête LIT sans rien modifier. Contrairement aux commandes,
// le côté lecture COURT-CIRCUITE le domaine : il ne charge pas l'agrégat Order mais
// projette directement vers des ViewModels (DTOs taillés pour l'affichage). Cela évite
// d'exposer le modèle riche au client et permet d'optimiser la lecture indépendamment
// de l'écriture — c'est tout l'intérêt de séparer Command et Query (CQRS).
public record GetOrdersQuery(string BuyerId) : IRequest<List<OrderViewModel>>;

// ViewModel de lecture : structure plate et sérialisable, découplée de l'agrégat Order.
public record OrderViewModel
{
    public int Id { get; init; }
    public string BuyerId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public DateTime OrderDate { get; init; }
    public List<OrderItemViewModel> Items { get; init; } = [];
}

public record OrderItemViewModel
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Units { get; init; }
}