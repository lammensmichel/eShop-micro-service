using System.Text.Json;
using Basket.API.Models;
using StackExchange.Redis;

namespace Basket.API.Repositories;

// Implémentation Redis du dépôt de paniers. Modèle de stockage très simple :
// une clé string par acheteur (clé = BuyerId) dont la valeur est le panier
// sérialisé en JSON. Redis convient ici car le panier est une donnée volatile,
// fortement sollicitée en lecture/écriture, et qui n'a pas besoin de requêtes
// relationnelles ni de transactions complexes.
public class RedisBasketRepository : IBasketRepository
{
    private readonly IDatabase _database;

    public RedisBasketRepository(IConnectionMultiplexer redis)
    {
        // IConnectionMultiplexer est partagé (singleton, fourni par AddRedisClient) ;
        // GetDatabase() renvoie un handle léger vers la base logique par défaut.
        _database = redis.GetDatabase();
    }

// Lecture : on récupère la valeur brute (string GET), null si la clé n'existe pas
// ou a expiré, sinon on désérialise le JSON vers CustomerBasket.
public async Task<CustomerBasket?> GetBasketAsync(string buyerId)
{
    var data = await _database.StringGetAsync(buyerId);
    if (data.IsNullOrEmpty) return null;
    return JsonSerializer.Deserialize<CustomerBasket>(data.ToString());
}

    public async Task<CustomerBasket> UpdateBasketAsync(CustomerBasket basket)
    {
        // SET clé=BuyerId avec une expiration glissante de 30 jours : un panier
        // abandonné finit par disparaître tout seul, sans tâche de nettoyage.
        // Chaque mise à jour réécrit l'objet complet (pas de fusion incrémentale).
        var json = JsonSerializer.Serialize(basket);
        await _database.StringSetAsync(basket.BuyerId, json, TimeSpan.FromDays(30));
        return basket;
    }

    public async Task DeleteBasketAsync(string buyerId)
    {
        // DEL : appelé après un checkout confirmé pour vider le panier de l'acheteur.
        await _database.KeyDeleteAsync(buyerId);
    }
}