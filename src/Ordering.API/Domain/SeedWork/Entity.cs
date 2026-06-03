namespace Ordering.API.Domain.SeedWork;

// Classe de base de toute entité du domaine (DDD). Une entité a une IDENTITÉ stable
// (ici Id) : deux entités sont « la même » si elles ont le même Id, indépendamment de
// leurs autres attributs — par opposition à un ValueObject, comparé par sa valeur.
//
// Entity porte aussi la liste des DOMAIN EVENTS levés par l'agrégat. Ce sont des faits
// métier survenus dans le modèle (« commande passée », « stock confirmé »...) accumulés
// en mémoire pendant l'exécution d'une méthode du domaine, et publiés PLUS TARD (après
// le SaveChanges, voir OrderingDbContext) plutôt qu'immédiatement. Cela découple l'agrégat
// de ses effets de bord (logs, events d'intégration...) et garantit qu'ils ne partent que
// si la transaction réussit.
public abstract class Entity
{
    // Id à setter protégé : seules l'entité elle-même (et EF Core) peuvent l'affecter.
    // Pour une entité neuve, il vaut 0 jusqu'à ce que la base génère la clé au SaveChanges.
    public int Id { get; protected set; }

    // Liste paresseuse (nullable) : on n'alloue rien tant qu'aucun event n'est levé.
    private List<IDomainEvent>? _domainEvents;

    // Exposée en lecture seule : seul le domaine ajoute via AddDomainEvent ; le reste
    // du code (DbContext) ne fait que lire puis vider la liste.
    public IReadOnlyCollection<IDomainEvent> DomainEvents =>
        _domainEvents?.AsReadOnly() ?? new List<IDomainEvent>().AsReadOnly();

    // protected : seul l'agrégat lui-même lève ses events, depuis ses méthodes métier
    // (constructeur, transitions d'état). On n'ajoute jamais un event « de l'extérieur ».
    protected void AddDomainEvent(IDomainEvent eventItem)
    {
        _domainEvents ??= [];
        _domainEvents.Add(eventItem);
    }

    // Appelée par le dispatch après publication des events, pour éviter de les rejouer.
    public void ClearDomainEvents() => _domainEvents?.Clear();
}