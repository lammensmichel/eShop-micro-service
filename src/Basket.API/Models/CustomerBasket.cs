namespace Basket.API.Models;

// =============================================================================
// FICHIER : CustomerBasket.cs
// RÔLE    : le modèle de données central du service : le panier complet d'un acheteur.
// CONCEPT : AGRÉGAT DE CACHE (clé-valeur) plutôt que modèle relationnel.
//
//   C'est l'UNITÉ stockée telle quelle dans Redis : l'objet entier est sérialisé
//   en JSON sous une clé = BuyerId (cf. RedisBasketRepository). Basket.API ne
//   possède pas de base relationnelle. Un panier est une donnée volatile, très
//   sollicitée, sans besoin de requêtes complexes -> un "cache clé-valeur" comme
//   Redis (base en mémoire, clé → valeur) est idéal : rapide et avec expiration
//   automatique. À comparer avec Ordering.API qui, lui, utilise Postgres car une
//   commande est une donnée durable et transactionnelle.
//
// À LIRE :
//   - AVANT : BasketItem.cs (les lignes que ce panier contient).
//   - APRÈS : Repositories/RedisBasketRepository.cs (comment il est persisté).
// =============================================================================
public class CustomerBasket
{
    // Identifiant de l'acheteur = clé Redis. Côté API il est TOUJOURS dérivé du
    // jeton JWT (claim "sub"), jamais d'une valeur fournie librement par le client
    // (protection anti-IDOR, cf. BasketApi.GetBuyerId).
    public required string BuyerId { get; set; }
    public List<BasketItem> Items { get; set; } = [];

    // Total calculé à la volée : aucune valeur stockée à maintenir cohérente,
    // on évite tout risque de désynchronisation entre les lignes et le total.
    public decimal TotalPrice => Items.Sum(i => i.TotalPrice);
}