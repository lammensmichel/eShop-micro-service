using MediatR;

namespace Ordering.API.Application.Commands;

// Côté COMMAND du CQRS : une commande exprime une INTENTION de modifier l'état (créer une
// commande). Elle implémente IRequest<int> -> MediatR la route vers UN seul handler
// (CreateOrderCommandHandler) qui renvoie l'Id créé. C'est un DTO d'application, distinct
// de l'agrégat Order : il porte des données brutes (pas d'invariants), validées par le
// pipeline (CreateOrderCommandValidator) avant d'atteindre le domaine.
public record CreateOrderCommand : IRequest<int>
{
    public required string BuyerId { get; init; }
    public required string City { get; init; }
    public required string Street { get; init; }
    public required string Country { get; init; }
    public required string ZipCode { get; init; }
    public List<CreateOrderCommandItem> Items { get; init; } = [];
}

public record CreateOrderCommandItem
{
    public int ProductId { get; init; }
    public required string ProductName { get; init; }
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
}