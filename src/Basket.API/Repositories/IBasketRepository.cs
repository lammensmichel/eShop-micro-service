using Basket.API.Models;

namespace Basket.API.Repositories;

// =============================================================================
// FICHIER : IBasketRepository.cs
// RÔLE    : le "port" de persistance du panier. Les endpoints en dépendent.
// CONCEPT : PATTERN REPOSITORY + INVERSION DE DÉPENDANCE.
//
//   Un "repository" abstrait le stockage derrière des opérations métier (lire,
//   enregistrer, supprimer un panier) sans exposer la technologie sous-jacente.
//   BasketApi dépend de CETTE interface, pas de Redis : on pourrait remplacer
//   l'implémentation (mémoire, SQL, autre cache) sans toucher aux endpoints, et on
//   peut fournir un faux en test. Même logique de découplage que IEventBus.
//
// À LIRE : juste avant Repositories/RedisBasketRepository.cs (l'implémentation).
// =============================================================================
public interface IBasketRepository
{
    // Retourne null si l'acheteur n'a aucun panier (cache vide ou expiré).
    Task<CustomerBasket?> GetBasketAsync(string buyerId);

    // Crée ou remplace intégralement le panier (sémantique "upsert", pas de fusion).
    Task<CustomerBasket> UpdateBasketAsync(CustomerBasket basket);

    // Supprime le panier (notamment après un checkout réussi).
    Task DeleteBasketAsync(string buyerId);
}