using Ordering.API.Domain.SeedWork;

namespace Ordering.API.Domain.AggregatesModel.OrderAggregate;

// Moyen de paiement de la commande : VALUE OBJECT (mêmes conventions qu'Address). Pas
// d'identité propre — deux moyens de paiement aux mêmes composantes sont « le même ». En
// base, il n'a pas sa propre table : il est « possédé » par Order et ses colonnes sont
// aplaties dans la table Orders (OwnsOne, voir le DbContext).
//
// ⚠️ AVERTISSEMENT PCI-DSS — À LIRE AVANT TOUT VRAI PAIEMENT :
// Stocker (ou faire transiter) un numéro de carte EN CLAIR (le PAN, Primary Account Number)
// est STRICTEMENT INTERDIT par la norme PCI-DSS en production. Ici, on conserve CardNumber
// en clair UNIQUEMENT pour la simulation pédagogique du parcours de paiement.
// En production, on ne stockerait JAMAIS le PAN : au moment du checkout, on TOKENISE la
// carte chez le prestataire de paiement (Stripe, Adyen...) qui renvoie un JETON opaque.
// Seul ce jeton transiterait par nos services et par le bus ; le PAN ne nous toucherait
// jamais, ce qui réduit drastiquement le périmètre PCI-DSS. Ce value object serait alors
// remplacé par un simple identifiant de jeton (ex. PaymentTokenId).
public class PaymentMethod : ValueObject
{
    public string CardNumber { get; private set; } = string.Empty;
    public string CardHolderName { get; private set; } = string.Empty;
    public DateTime CardExpiration { get; private set; }

    // Constructeur réservé à EF Core (matérialisation).
    protected PaymentMethod() { }

    public PaymentMethod(string cardNumber, string cardHolderName, DateTime cardExpiration)
    {
        CardNumber = cardNumber;
        CardHolderName = cardHolderName;
        CardExpiration = cardExpiration;
    }

    // Toutes les composantes participent à l'égalité par valeur.
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return CardNumber;
        yield return CardHolderName;
        yield return CardExpiration;
    }
}
