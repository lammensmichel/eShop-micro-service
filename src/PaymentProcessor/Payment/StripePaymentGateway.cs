using Microsoft.Extensions.Configuration;

namespace PaymentProcessor.Payment;

// ============================================================================
// StripePaymentGateway — SQUELETTE d'une VRAIE passerelle de paiement.
// ----------------------------------------------------------------------------
// ⚠️ NON BRANCHÉ par défaut. La simulation (SimulatedPaymentGateway) reste
// l'implémentation active. Cette classe sert de POINT DE DÉPART concret pour
// intégrer un vrai prestataire (ici Stripe, mais le principe vaut pour Adyen,
// Mollie, etc.). Tant que ChargeAsync n'est pas implémentée, ne l'enregistre pas.
//
// POURQUOI ce fichier ? Pour montrer que l'abstraction IPaymentGateway tient sa
// promesse : passer en production réelle = (1) implémenter cette classe,
// (2) changer UNE ligne de DI dans Program.cs. Le worker, la saga, le contrat
// d'événements et le reste du système ne bougent pas.
//
// ────────────────────────────────────────────────────────────────────────────
// POUR ACTIVER UN VRAI PAIEMENT STRIPE — checklist :
//
//  1) Ajouter le SDK : `dotnet add src/PaymentProcessor package Stripe.net`
//     (ou utiliser un HttpClient typé vers https://api.stripe.com/v1).
//
//  2) Fournir la clé secrète Stripe par CONFIG/SECRET (jamais en dur) :
//     - en dev : user-secrets / appsettings.Development.json -> "Stripe:SecretKey".
//     - en prod K8s : l'ajouter au Secret `eshop-secrets` (clé `stripe-secret-key`)
//       et l'injecter au worker en env `Stripe__SecretKey` (voir k8s/CONTRACT.md
//       et le Deployment paymentprocessor).
//
//  3) ⚠️ PCI-DSS — LE POINT CRUCIAL :
//     NE JAMAIS faire transiter / stocker le numéro de carte (PAN) EN CLAIR par
//     nos services. Aujourd'hui PaymentRequest.CardNumber EST le PAN brut, parce
//     que c'était commode pour la SIMULATION. Avec un vrai prestataire, le schéma
//     change : la carte est TOKENISÉE côté NAVIGATEUR (Stripe.js / Stripe Elements)
//     AU MOMENT DU CHECKOUT. Le front n'envoie alors qu'un *PaymentMethod token*
//     (ex. "pm_xxx"), pas le PAN. Ce token remonte dans la saga à la place des
//     champs carte, et c'est LUI qu'on transmet à Stripe ici.
//     => Migration recommandée : remplacer (CardNumber/CardHolderName/CardExpiration)
//        par un `PaymentMethodToken` dans BasketCheckoutEvent -> Order ->
//        OrderStockConfirmedIntegrationEvent -> PaymentRequest. Notre périmètre PCI
//        se réduit alors quasiment à zéro (on ne voit jamais la carte).
//
//  4) Enregistrer dans Program.cs (remplacer la ligne de la simulation) :
//        builder.Services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
//     (et, si HttpClient typé : builder.Services.AddHttpClient<StripePaymentGateway>().)
//
//  5) Idempotence prestataire : passer une clé d'idempotence à Stripe (ex.
//     l'Id de l'événement OrderStockConfirmed, ou OrderId) pour qu'un rejeu du
//     message ne débite pas deux fois — Stripe déduplique sur cette clé.
// ============================================================================
public class StripePaymentGateway : IPaymentGateway
{
    private readonly ILogger<StripePaymentGateway> _logger;
    private readonly string? _secretKey;

    public StripePaymentGateway(
        IConfiguration configuration,
        ILogger<StripePaymentGateway> logger)
    {
        _logger = logger;
        // Clé secrète Stripe lue depuis la configuration (jamais en dur). En prod,
        // elle vient d'un Secret Kubernetes injecté en variable d'environnement
        // `Stripe__SecretKey`. Si elle est absente, l'intégration n'est pas configurée.
        _secretKey = configuration["Stripe:SecretKey"];
    }

    public Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken = default)
    {
        // Garde de configuration : sans clé secrète, on ne peut pas appeler Stripe.
        if (string.IsNullOrWhiteSpace(_secretKey))
        {
            throw new InvalidOperationException(
                "Stripe:SecretKey non configurée — l'intégration Stripe n'est pas prête. " +
                "Renseigne la clé (Secret/env) avant d'enregistrer StripePaymentGateway.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // STRUCTURE D'UNE IMPLÉMENTATION RÉELLE (à compléter) :
        //
        //   // (a) Idempotence : une clé stable par tentative de paiement.
        //   var idempotencyKey = $"order-{request.OrderId}";
        //
        //   // (b) Créer/confirmer un PaymentIntent avec le SDK Stripe.net.
        //   //     NB : on passe le TOKEN de moyen de paiement (pm_xxx) obtenu côté
        //   //     navigateur, PAS le PAN. (Voir la note PCI en tête de fichier ;
        //   //     PaymentRequest devra porter ce token plutôt que CardNumber.)
        //   StripeConfiguration.ApiKey = _secretKey;
        //   var service = new PaymentIntentService();
        //   var options = new PaymentIntentCreateOptions
        //   {
        //       Amount = (long)(request.Amount * 100), // Stripe attend des centimes
        //       Currency = "eur",
        //       PaymentMethod = request.PaymentMethodToken, // <-- token, pas le PAN
        //       Confirm = true,
        //       Description = $"eShop order {request.OrderId}",
        //   };
        //   var requestOptions = new RequestOptions { IdempotencyKey = idempotencyKey };
        //
        //   try
        //   {
        //       var intent = await service.CreateAsync(options, requestOptions, cancellationToken);
        //       return intent.Status == "succeeded"
        //           ? PaymentResult.Success(intent.Id)                 // reçu = id Stripe
        //           : PaymentResult.Failure($"Paiement non confirmé ({intent.Status})");
        //   }
        //   catch (StripeException ex) when (ex.StripeError?.Type == "card_error")
        //   {
        //       // Refus MÉTIER (carte refusée, fonds insuffisants...) -> Failure,
        //       // pas d'exception : la saga annulera proprement la commande.
        //       return PaymentResult.Failure(ex.StripeError.Message);
        //   }
        //   // Toute autre exception (réseau, Stripe indisponible) REMONTE : c'est une
        //   // erreur TRANSITOIRE -> le worker nack/requeue et réessaiera plus tard.
        // ─────────────────────────────────────────────────────────────────────

        _logger.LogWarning(
            "StripePaymentGateway est un squelette non implémenté (commande {OrderId}).",
            request.OrderId);

        // Tant que l'intégration n'est pas écrite, on échoue FRANCHEMENT : on ne veut
        // pas qu'un déploiement enregistre cette classe par erreur et « confirme »
        // des paiements fantômes. (NotImplementedException = erreur transitoire côté
        // worker -> nack/requeue, ce qui rend le problème très visible en logs.)
        throw new NotImplementedException(
            "StripePaymentGateway.ChargeAsync n'est pas implémentée — voir la checklist " +
            "en tête de fichier (SDK Stripe.net, tokenisation PCI, enregistrement DI).");
    }
}
