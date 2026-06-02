namespace eShop.IntegrationEvents.Messaging;

/// <summary>
/// Classe de base de tout événement d'intégration publié sur le bus partagé.
/// Porte un identifiant unique (<see cref="Id"/>) servant de clé d'idempotence
/// côté consommateur, ainsi que sa date de création (UTC).
/// </summary>
/// <remarks>
/// Les valeurs par défaut (nouveau Guid / date courante) sont initialisées dans le
/// constructeur sans paramètre plutôt que dans des initialiseurs de propriété, afin
/// de ne pas interférer avec la désérialisation : System.Text.Json appelle d'abord ce
/// constructeur, puis réaffecte les propriétés <c>init</c> présentes dans le JSON.
/// L'identifiant émis à la publication est donc bien préservé à la réception.
/// </remarks>
public abstract record IntegrationEvent
{
    protected IntegrationEvent()
    {
        Id = Guid.NewGuid();
        CreationDate = DateTime.UtcNow;
    }

    public Guid Id { get; init; }

    public DateTime CreationDate { get; init; }
}
