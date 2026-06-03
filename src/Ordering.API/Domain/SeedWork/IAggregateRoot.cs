namespace Ordering.API.Domain.SeedWork;

// Interface marqueur (sans membre) désignant une RACINE D'AGRÉGAT (DDD).
// Un agrégat est un groupe d'objets du domaine traité comme une seule unité de cohérence ;
// sa racine est le SEUL point d'entrée autorisé pour le modifier (ex. Order contrôle ses
// OrderItem). C'est aussi la frontière transactionnelle : on charge/persiste l'agrégat
// entier. La contrainte `where T : IAggregateRoot` sur IRepository<T> garantit qu'on ne
// crée un repository QUE pour des racines d'agrégat, jamais pour des entités internes.
public interface IAggregateRoot
{
}