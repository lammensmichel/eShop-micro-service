using Basket.API.Models;

namespace Basket.API.Repositories;

// Abstraction du stockage du panier : l'API dépend de cette interface, pas de Redis.
// On pourrait remplacer l'implémentation (Redis aujourd'hui) sans toucher aux endpoints.
public interface IBasketRepository
{
    // Retourne null si l'acheteur n'a aucun panier (cache vide ou expiré).
    Task<CustomerBasket?> GetBasketAsync(string buyerId);

    // Crée ou remplace intégralement le panier (sémantique "upsert", pas de fusion).
    Task<CustomerBasket> UpdateBasketAsync(CustomerBasket basket);

    // Supprime le panier (notamment après un checkout réussi).
    Task DeleteBasketAsync(string buyerId);
}