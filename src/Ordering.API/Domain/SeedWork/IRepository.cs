namespace Ordering.API.Domain.SeedWork;

// Abstraction de PERSISTANCE par agrégat (pattern Repository, DDD). Le domaine définit
// l'interface ; l'implémentation concrète (OrderRepository, EF Core) vit dans Infrastructure
// -> inversion de dépendance : le domaine ne connaît pas la base de données.
// La contrainte `where T : IAggregateRoot` impose un repository PAR racine d'agrégat
// (un agrégat = une unité de chargement/sauvegarde), jamais par entité interne.
public interface IRepository<T> where T : IAggregateRoot
{
    // Charge l'agrégat COMPLET (avec ses entités enfants) ou null s'il n'existe pas.
    Task<T?> GetAsync(int id);
    T Add(T aggregate);
    void Update(T aggregate);
    // Valide l'unité de travail. C'est ce SaveChanges qui, côté implémentation,
    // déclenche la persistance ET le dispatch des domain events accumulés.
    Task<int> SaveChangesAsync();
}