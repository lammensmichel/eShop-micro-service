namespace Ordering.API.Domain.SeedWork;

// Classe de base des VALUE OBJECTS (DDD). Un value object n'a PAS d'identité propre :
// il est défini uniquement par sa valeur (ex. Address, OrderStatus). Deux value objects
// sont égaux si TOUTES leurs composantes sont égales, peu importe la référence mémoire.
// Conséquences : ils sont conceptuellement immuables et interchangeables.
// On centralise ici l'égalité par valeur ; chaque sous-classe n'a qu'à déclarer ses
// composantes significatives via GetEqualityComponents().
public abstract class ValueObject
{
    // Composantes qui définissent l'égalité (l'ordre compte). Chaque sous-classe les fournit.
    protected abstract IEnumerable<object> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj == null || obj.GetType() != GetType())
            return false;

        // Égalité STRUCTURELLE : on compare les composantes deux à deux, dans l'ordre.
        return GetEqualityComponents()
            .SequenceEqual(((ValueObject)obj).GetEqualityComponents());
    }

    // Hash dérivé des mêmes composantes : cohérent avec Equals (contrat .NET respecté).
    public override int GetHashCode() =>
        GetEqualityComponents()
            .Aggregate(1, (current, obj) =>
                HashCode.Combine(current, obj.GetHashCode()));
}