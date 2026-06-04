using System.Security.Claims;
using Basket.API.Models;
using Basket.API.Repositories;
using eShop.IntegrationEvents.Events;
using eShop.IntegrationEvents.Messaging;

namespace Basket.API.Apis;

// =============================================================================
// FICHIER : BasketApi.cs
// RÔLE    : la "façade" HTTP du service Basket. Définit les endpoints REST du
//           panier (lire, mettre à jour, checkout, supprimer).
// CONCEPT : MINIMAL API + SÉCURITÉ ANTI-IDOR + FRONTIÈRE ÉVÉNEMENTIELLE.
//
//   - "Minimal API" : style ASP.NET Core sans contrôleurs ni classes. Les routes
//     sont déclarées par app.MapGet/MapPost(...) et les handlers sont des lambdas.
//     Leurs paramètres sont fournis par l'INJECTION DE DÉPENDANCES (ex. demander
//     IBasketRepository en argument suffit pour le recevoir). Plus léger qu'un
//     ControllerBase pour un microservice à quelques endpoints.
//
//   - "Anti-IDOR" (Insecure Direct Object Reference) : faille où un utilisateur
//     accède à la ressource d'autrui en devinant/modifiant un identifiant dans
//     l'URL (ex. /api/basket/<id-d-un-autre>). PARADE appliquée ici : on N'utilise
//     JAMAIS l'identifiant fourni par le client. Le buyerId est toujours dérivé du
//     jeton JWT (cf. GetBuyerId). Le {buyerId} de l'URL est gardé pour compatibilité
//     du front mais IGNORÉ.
//
//   - C'est ce service qui contient la FRONTIÈRE entre le monde synchrone (HTTP) et
//     le monde asynchrone (messagerie) : voir /checkout, qui publie un événement.
//
// PLACE DANS LE FLUX : enregistré dans Program.cs via
//   MapBasketApi().RequireAuthorization() -> TOUT le groupe exige un jeton valide.
//   L'endpoint /checkout est le point de départ de la saga (cf. BasketCheckoutEvent).
//
// À LIRE :
//   - AVANT : Models/CustomerBasket.cs, Models/BasketItem.cs (les données manipulées),
//             Repositories/IBasketRepository.cs (le stockage).
//   - APRÈS : eShop.IntegrationEvents/Messaging/RabbitMQPublisher.cs (où va l'événement
//             publié au checkout), puis Ordering.API/.../RabbitMQConsumer.cs (réception).
// =============================================================================
public static class BasketApi
{
    public static RouteGroupBuilder MapBasketApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/basket");

        // Le paramètre {buyerId} de l'URL est conservé pour ne pas casser le front,
        // mais il est IGNORÉ : le buyerId est toujours dérivé du jeton (anti-IDOR).
        group.MapGet("/{buyerId}", async (string buyerId, ClaimsPrincipal user, IBasketRepository repository) =>
        {
            var tokenBuyerId = GetBuyerId(user);
            if (tokenBuyerId is null) return Results.Unauthorized();

            var basket = await repository.GetBasketAsync(tokenBuyerId);
            return basket is null
                ? Results.Ok(new CustomerBasket { BuyerId = tokenBuyerId })
                : Results.Ok(basket);
        });

        group.MapPost("/", async (CustomerBasket basket, ClaimsPrincipal user, IBasketRepository repository) =>
        {
            var tokenBuyerId = GetBuyerId(user);
            if (tokenBuyerId is null) return Results.Unauthorized();

            // On force le BuyerId fourni par le client à celui du jeton (anti-IDOR).
            basket.BuyerId = tokenBuyerId;

            var updated = await repository.UpdateBasketAsync(basket);
            return Results.Ok(updated);
        });

        group.MapPost("/checkout", async (
            BasketCheckout checkout,
            ClaimsPrincipal user,
            IBasketRepository repository,
            IEventBus publisher) =>
        {
            var tokenBuyerId = GetBuyerId(user);
            if (tokenBuyerId is null) return Results.Unauthorized();

            var basket = await repository.GetBasketAsync(tokenBuyerId);
            if (basket is null) return Results.NotFound();

            // Checkout = frontière entre services. Basket.API ne crée PAS la commande
            // lui-même : il publie un événement d'intégration (BasketCheckoutEvent) sur
            // le bus RabbitMQ, qu'Ordering.API consomme pour créer la commande. On compose
            // le message à partir du contenu du panier (source serveur) et des infos de
            // checkout (corps de requête). C'est le découplage typique d'une architecture
            // microservices pilotée par événements.
            var eventMessage = new BasketCheckoutEvent
            {
                BuyerId = tokenBuyerId,
                City = checkout.City,
                Street = checkout.Street,
                Country = checkout.Country,
                ZipCode = checkout.ZipCode,
                CardNumber = checkout.CardNumber,
                CardHolderName = checkout.CardHolderName,
                CardExpiration = checkout.CardExpiration,
                Total = basket.TotalPrice,
                Items = basket.Items.Select(i => new BasketCheckoutItem
                {
                    ProductId = i.ProductId,
                    ProductName = i.ProductName,
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Quantity
                }).ToList()
            };

            // ORDRE CRUCIAL : on PUBLIE D'ABORD, on supprime le panier ENSUITE.
            // Pourquoi cet ordre ? Si on vidait le panier avant de publier et que la
            // publication échouait, on aurait perdu le panier ET l'événement : la
            // commande s'évanouit. À l'inverse, en publiant d'abord :
            //   - le publisher utilise publisher confirms + mandatory:true (cf.
            //     RabbitMQPublisher) ; si le message n'est pas confirmé/routable,
            //     PublishAsync LÈVE une exception AVANT le delete ;
            //   - donc en cas d'échec le panier est intact et le client peut
            //     re-checkouter sans rien perdre.
            // C'est un compromis classique : on préfère un éventuel doublon (gérable
            // par l'idempotence côté consommateur) à une perte de données.
            await publisher.PublishAsync(eventMessage, "basket-checkout");

            // À ce stade le broker a accusé réception de l'événement (commande prise
            // en charge). Si la suppression du panier échoue, on NE propage PAS l'erreur :
            // renvoyer un échec inciterait le client à re-checkouter, ce qui republierait
            // l'événement (BasketCheckoutEvent.Id est régénéré à chaque appel et ne protège
            // donc pas contre les doublons côté Ordering.API). Un panier résiduel est
            // bénin et sera écrasé/vidé au prochain usage.
            try
            {
                await repository.DeleteBasketAsync(tokenBuyerId);
            }
            catch
            {
                // Suppression best-effort : l'événement est déjà confirmé, on ne rejoue rien.
            }

            return Results.Accepted();
        });

        group.MapDelete("/{buyerId}", async (string buyerId, ClaimsPrincipal user, IBasketRepository repository) =>
        {
            var tokenBuyerId = GetBuyerId(user);
            if (tokenBuyerId is null) return Results.Unauthorized();

            await repository.DeleteBasketAsync(tokenBuyerId);
            return Results.NoContent();
        });

        return group;
    }

    // CŒUR DE LA PARADE ANTI-IDOR : la seule source autorisée du buyerId.
    // Le ClaimsPrincipal est construit par ASP.NET à partir du jeton JWT validé
    // (cf. AddDefaultAuthentication dans Program.cs). On lit le claim standard OIDC
    // "sub" (subject = identifiant de l'utilisateur connecté), avec repli sur
    // NameIdentifier selon la façon dont le jeton est mappé. Comme cette valeur vient
    // du jeton signé par Identity.API et NON du client, elle n'est pas falsifiable :
    // impossible d'agir sur le panier d'autrui.
    private static string? GetBuyerId(ClaimsPrincipal user)
        => user.FindFirst("sub")?.Value ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
}
