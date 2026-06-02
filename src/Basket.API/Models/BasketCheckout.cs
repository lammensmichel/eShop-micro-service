namespace Basket.API.Models;

public class BasketCheckout
{
    public required string BuyerId { get; set; }
    public required string City { get; set; }
    public required string Street { get; set; }
    public required string Country { get; set; }
    public required string ZipCode { get; set; }
    public required string CardNumber { get; set; }
    public required string CardHolderName { get; set; }
    public DateTime CardExpiration { get; set; }
}