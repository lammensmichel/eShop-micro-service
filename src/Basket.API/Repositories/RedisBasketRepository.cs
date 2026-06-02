using System.Text.Json;
using Basket.API.Models;
using StackExchange.Redis;

namespace Basket.API.Repositories;

public class RedisBasketRepository : IBasketRepository
{
    private readonly IDatabase _database;

    public RedisBasketRepository(IConnectionMultiplexer redis)
    {
        _database = redis.GetDatabase();
    }

public async Task<CustomerBasket?> GetBasketAsync(string buyerId)
{
    var data = await _database.StringGetAsync(buyerId);
    if (data.IsNullOrEmpty) return null;
    return JsonSerializer.Deserialize<CustomerBasket>(data.ToString());
}

    public async Task<CustomerBasket> UpdateBasketAsync(CustomerBasket basket)
    {
        var json = JsonSerializer.Serialize(basket);
        await _database.StringSetAsync(basket.BuyerId, json, TimeSpan.FromDays(30));
        return basket;
    }

    public async Task DeleteBasketAsync(string buyerId)
    {
        await _database.KeyDeleteAsync(buyerId);
    }
}