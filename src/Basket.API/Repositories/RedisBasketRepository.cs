using System.Text.Json;
using Basket.API.Models;
using StackExchange.Redis;

namespace Basket.API.Repositories;

// =============================================================================
// FICHIER : RedisBasketRepository.cs
// RÔLE    : implémentation concrète d'IBasketRepository au-dessus de Redis.
// CONCEPT : "PANIER EN CACHE REDIS" — stockage clé-valeur + expiration (TTL).
//
//   Redis est une base de données EN MÉMOIRE (très rapide) organisée en couples
//   clé → valeur. Ici le modèle est minimal :
//       clé   = BuyerId
//       valeur= le CustomerBasket entier sérialisé en JSON
//   Trois opérations seulement : GET (lire), SET avec durée de vie (écrire),
//   DEL (supprimer). Pas de jointures, pas de transactions complexes : le panier
//   n'en a pas besoin, et on gagne en simplicité/performance.
//
//   "TTL" (Time To Live) : le SET pose une expiration (30 j ici). Un panier
//   abandonné s'efface tout seul — pas de tâche de ménage à écrire. C'est un
//   avantage structurel du cache pour une donnée volatile.
//
//   La connexion Redis (IConnectionMultiplexer) est un SINGLETON thread-safe
//   fourni par AddRedisClient (cf. Program.cs) : on la partage entre toutes les
//   requêtes ; GetDatabase() ne renvoie qu'un handle léger.
//
// À LIRE :
//   - AVANT : IBasketRepository.cs (le contrat), CustomerBasket.cs (l'objet stocké).
//   - APRÈS : Program.cs (où AddRedisClient et ce dépôt sont enregistrés).
// =============================================================================
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