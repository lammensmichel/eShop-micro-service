namespace WebApp.Models;

// DTO partagés des commandes, utilisés par la page Orders.razor.
public record OrderDto(
    int Id,
    string BuyerId,
    string Status,
    decimal Total,
    DateTime OrderDate,
    List<OrderItemDto> Items);

public record OrderItemDto(int ProductId, string ProductName, decimal UnitPrice, int Units);
