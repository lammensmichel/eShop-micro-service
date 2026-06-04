namespace PaymentProcessor.Payment;

// ============================================================================
// PaymentModels — les TYPES DE DONNÉES échangés avec la passerelle de paiement.
// ----------------------------------------------------------------------------
// Ces records sont volontairement INDÉPENDANTS des événements d'intégration :
// le worker traduit l'événement reçu en PaymentRequest, et le PaymentResult en
// événement publié. Ainsi la passerelle ne connaît ni RabbitMQ ni la saga —
// elle ne parle que « paiement » (séparation des responsabilités).
// ============================================================================

/// <summary>
/// Ce qu'on demande à la passerelle : montant + moyen de paiement.
/// </summary>
/// <remarks>
/// ⚠️ PCI-DSS : CardNumber est ici le PAN EN CLAIR, uniquement pour la
/// simulation. En production on passerait un JETON (token) obtenu auprès du
/// prestataire, jamais le numéro brut.
/// </remarks>
public record PaymentRequest(
    int OrderId,
    string BuyerId,
    decimal Amount,
    string CardNumber,
    string CardHolderName,
    DateTime CardExpiration);

/// <summary>
/// Le résultat d'une tentative de paiement. Soit un succès (avec un
/// TransactionId, le « reçu »), soit un échec (avec un motif explicatif).
/// Les deux cas sont mutuellement exclusifs.
/// </summary>
public record PaymentResult
{
    // true = débit accepté ; false = refusé.
    public bool Succeeded { get; init; }

    // Référence de transaction renvoyée par la passerelle en cas de succès
    // (null en cas d'échec).
    public string? TransactionId { get; init; }

    // Motif d'échec lisible en cas de refus (null en cas de succès).
    public string? FailureReason { get; init; }

    // Fabriques statiques : on force la construction par ces deux portes
    // d'entrée pour garantir des résultats COHÉRENTS (un succès a toujours un
    // TransactionId, un échec a toujours un motif). On évite ainsi de pouvoir
    // créer un « succès sans transaction » ou un « échec sans raison ».
    public static PaymentResult Success(string transactionId) =>
        new() { Succeeded = true, TransactionId = transactionId };

    public static PaymentResult Failure(string reason) =>
        new() { Succeeded = false, FailureReason = reason };
}
