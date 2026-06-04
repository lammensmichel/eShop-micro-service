namespace PaymentProcessor.Payment;

// ============================================================================
// IPaymentGateway — l'ABSTRACTION (le « port ») du paiement.
// ----------------------------------------------------------------------------
// RÔLE : représenter « débiter un moyen de paiement » comme un CONTRAT, sans
// dire COMMENT. Le PaymentWorker dépend de cette interface, jamais d'une classe
// concrète. C'est le « D » de SOLID (Dependency Inversion) : le métier dépend
// d'une abstraction, pas d'un détail technique.
//
// POINT D'EXTENSION — c'est ICI qu'on branche un vrai prestataire.
//   Aujourd'hui, l'unique implémentation est SimulatedPaymentGateway (une
//   simulation, par défaut dans le projet). Le jour où l'on veut un VRAI
//   paiement (Stripe, Adyen, Mollie...), on n'a RIEN à changer dans le worker :
//   il suffit
//     1. d'écrire une nouvelle classe, ex. StripePaymentGateway : IPaymentGateway,
//        qui fait un vrai appel HTTP à l'API du prestataire dans ChargeAsync ;
//     2. de changer UNE LIGNE d'enregistrement DI dans Program.cs
//        (AddSingleton<IPaymentGateway, StripePaymentGateway>()).
//   Tout le reste (consommation RabbitMQ, publication du résultat, saga) est
//   inchangé : c'est tout l'intérêt de passer par une abstraction remplaçable.
// ============================================================================
public interface IPaymentGateway
{
    /// <summary>
    /// Tente de débiter le moyen de paiement décrit par <paramref name="request"/>.
    /// Ne LÈVE PAS d'exception sur un refus métier (carte invalide, fonds, etc.) :
    /// renvoie un <see cref="PaymentResult"/> qui porte le succès OU l'échec. On
    /// réserve les exceptions aux erreurs TRANSITOIRES (réseau, prestataire down),
    /// que le worker traduira en nack/requeue.
    /// </summary>
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken = default);
}
