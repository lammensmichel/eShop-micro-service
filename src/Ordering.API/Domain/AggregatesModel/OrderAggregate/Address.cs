using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.AggregatesModel.OrderAggregate;

// Adresse de livraison : VALUE OBJECT. Pas d'identité propre — deux adresses identiques
// sont la même adresse. En base, elle n'a pas sa propre table : elle est « possédée » par
// Order et ses colonnes sont aplaties dans la table Orders (OwnsOne, voir le DbContext).
public class Address : ValueObject
{
    public string Street { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public string ZipCode { get; private set; } = string.Empty;

    // Constructeur réservé à EF Core (matérialisation).
    protected Address() { }

    public Address(string street, string city, string country, string zipCode)
    {
        Street = street;
        City = city;
        Country = country;
        ZipCode = zipCode;
    }

    // Toutes les composantes participent à l'égalité par valeur.
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return Country;
        yield return ZipCode;
    }
}