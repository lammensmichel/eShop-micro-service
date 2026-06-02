namespace Basket.API.Models;

public class CustomerBasket
{
    public required string BuyerId { get; set; }
    public List<BasketItem> Items { get; set; } = [];
    public decimal TotalPrice => Items.Sum(i => i.TotalPrice);
}