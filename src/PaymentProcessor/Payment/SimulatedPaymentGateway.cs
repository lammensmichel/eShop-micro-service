namespace PaymentProcessor.Payment;

// ============================================================================
// SimulatedPaymentGateway — l'implémentation par DÉFAUT (SIMULATION).
// ----------------------------------------------------------------------------
// RÔLE : jouer le rôle d'un prestataire de paiement SANS en appeler un vrai.
// C'est ce qui permet de faire tourner toute la saga de commande en local /
// en démo, sans compte Stripe ni vrai débit. Elle implémente IPaymentGateway,
// donc le worker ne voit AUCUNE différence avec un vrai prestataire.
//
// COMPORTEMENT :
//   1. Si l'option de config Payment:AlwaysFail = true -> échec systématique.
//      (interrupteur pratique pour exercer le chemin de COMPENSATION de la saga
//       — annulation de commande — sans avoir à fournir une carte invalide.)
//   2. Sinon, on VALIDE le moyen de paiement de façon réaliste (montant, nom,
//      carte via l'algorithme de Luhn, date d'expiration).
//   3. Si tout est valide -> succès avec un TransactionId factice.
//
// ⚠️ C'EST UNE SIMULATION : aucun appel réseau, aucun argent ne bouge. Pour un
// VRAI paiement, on écrirait une autre classe (ex. StripePaymentGateway) qui,
// à l'endroit marqué « ICI : appel HTTP » plus bas, ferait un POST vers l'API
// du prestataire (HttpClient), gérerait clé d'API, idempotency-key, etc., puis
// mapperait sa réponse en PaymentResult.Success / .Failure.
// ============================================================================
public class SimulatedPaymentGateway : IPaymentGateway
{
    private readonly ILogger<SimulatedPaymentGateway> _logger;
    private readonly bool _alwaysFail;

    public SimulatedPaymentGateway(IConfiguration configuration, ILogger<SimulatedPaymentGateway> logger)
    {
        _logger = logger;

        // L'interrupteur de simulation est lu ICI (dans la passerelle), et NON
        // plus dans le worker : c'est un détail du « comment on paie », donc sa
        // place est dans l'implémentation du paiement.
        _alwaysFail = configuration.GetValue("Payment:AlwaysFail", false);
    }

    public Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken = default)
    {
        // --- Interrupteur d'échec forcé (test du chemin de compensation) ---
        if (_alwaysFail)
        {
            _logger.LogWarning(
                "Simulation : Payment:AlwaysFail=true -> paiement refusé pour la commande {OrderId}",
                request.OrderId);
            return Task.FromResult(
                PaymentResult.Failure("Paiement refusé (simulation : Payment:AlwaysFail=true)"));
        }

        // --- Validation réaliste du moyen de paiement ---
        // On reproduit les refus métier qu'un vrai prestataire renverrait, pour
        // que la simulation soit crédible et exerce les deux branches de la saga.

        if (request.Amount <= 0)
        {
            return Task.FromResult(PaymentResult.Failure("Montant invalide"));
        }

        if (string.IsNullOrWhiteSpace(request.CardHolderName))
        {
            return Task.FromResult(PaymentResult.Failure("Titulaire de carte manquant"));
        }

        if (string.IsNullOrWhiteSpace(request.CardNumber) || !IsValidLuhn(request.CardNumber))
        {
            return Task.FromResult(PaymentResult.Failure("Numéro de carte invalide (Luhn)"));
        }

        // Carte expirée si la date d'expiration est dans le passé.
        if (request.CardExpiration < DateTime.UtcNow)
        {
            return Task.FromResult(PaymentResult.Failure("Carte expirée"));
        }

        // --- Tout est valide -> succès simulé ---
        // ICI, dans une VRAIE passerelle, on ferait l'appel HTTP au prestataire :
        //   var response = await _httpClient.PostAsJsonAsync("/v1/charges", body, cancellationToken);
        //   ... puis on mapperait response en PaymentResult.Success / .Failure.
        // En simulation, on fabrique simplement un identifiant de transaction factice.
        var transactionId = $"SIM-{Guid.NewGuid():N}";

        _logger.LogInformation(
            "Simulation : paiement accepté pour la commande {OrderId}, transaction {TransactionId}",
            request.OrderId, transactionId);

        return Task.FromResult(PaymentResult.Success(transactionId));
    }

    // ------------------------------------------------------------------------
    // Algorithme de LUHN (« somme de contrôle » des numéros de carte bancaire).
    // Principe : en partant de la droite, on double un chiffre sur deux ; si le
    // double dépasse 9 on lui retire 9 ; un numéro est valide si la somme totale
    // est un multiple de 10. C'est ce que font les vrais systèmes pour rejeter
    // immédiatement une faute de frappe (ce n'est PAS une garantie que la carte
    // existe — juste que le numéro est bien formé).
    // ------------------------------------------------------------------------
    private static bool IsValidLuhn(string cardNumber)
    {
        var sum = 0;
        var doubleDigit = false;

        // On parcourt les chiffres de DROITE à GAUCHE.
        for (var i = cardNumber.Length - 1; i >= 0; i--)
        {
            var c = cardNumber[i];

            // On ignore espaces/tirets éventuels ; tout autre caractère non
            // numérique rend le numéro invalide.
            if (c is ' ' or '-')
            {
                continue;
            }
            if (!char.IsDigit(c))
            {
                return false;
            }

            var digit = c - '0';

            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        // Numéro vide (que des séparateurs) -> invalide.
        return sum > 0 && sum % 10 == 0;
    }
}
