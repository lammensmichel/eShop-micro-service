using MediatR;

namespace Ordering.API.Application.Queries;

public record GetOrdersQuery(string BuyerId) : IRequest<List<OrderViewModel>>;

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