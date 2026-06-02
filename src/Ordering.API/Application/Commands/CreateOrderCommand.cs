using MediatR;

namespace Ordering.API.Application.Commands;

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