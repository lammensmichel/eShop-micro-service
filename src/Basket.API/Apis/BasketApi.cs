using System.Security.Claims;
using Basket.API.Models;
using Basket.API.Repositories;
using eShop.IntegrationEvents.Events;
using eShop.IntegrationEvents.Messaging;

namespace Basket.API.Apis;

// Groupe d'endpoints minimal-API du panier (style "minimal APIs" : pas de contrôleurs,
// les handlers sont des lambdas et leurs dépendances sont injectées par paramètre).
// Enregistré dans Program.cs via MapBasketApi().RequireAuthorization() : TOUT le groupe
// exige un jeton valide.
//
// Principe de sécurité transverse (anti-IDOR) : aucun endpoint ne fait confiance à un
// identifiant d'acheteur venant du client (paramètre d'URL ou corps de requête). Le
// buyerId réel est SYSTÉMATIQUEMENT dérivé du jeton JWT (cf. GetBuyerId). Sans cela, un
// utilisateur authentifié pourrait lire/modifier le panier d'autrui en changeant l'URL.
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

            // On publie d'abord, puis on supprime le panier. Le publisher utilise
            // désormais les publisher confirms + mandatory:true : si le message n'est
            // pas confirmé/routable, PublishAsync lève une exception AVANT le delete,
            // donc le panier est conservé et le client peut re-checkouter sans perte.
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

    // Le buyerId est toujours dérivé du jeton JWT (claim "sub", repli sur NameIdentifier).
    private static string? GetBuyerId(ClaimsPrincipal user)
        => user.FindFirst("sub")?.Value ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
}
