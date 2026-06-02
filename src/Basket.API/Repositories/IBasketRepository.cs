using Basket.API.Models;

namespace Basket.API.Repositories;

public interface IBasketRepository
{
    Task<CustomerBasket?> GetBasketAsync(string buyerId);
    Task<CustomerBasket> UpdateBasketAsync(CustomerBasket basket);
    Task DeleteBasketAsync(string buyerId);
}