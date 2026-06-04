using System.Security.Claims;
using MediatR;
using Ordering.API.Application.Commands;
using Ordering.API.Application.Queries;

namespace Ordering.API.Apis;

// COUCHE HTTP (Minimal APIs) : le point d'entrée SYNCHRONE du service, déclenché par
// l'utilisateur (le point d'entrée asynchrone, lui, est RabbitMQConsumer). Regroupe tous
// les endpoints sous /api/orders via une méthode d'extension appelée depuis Program.cs.
//
// Rôle volontairement MINCE : chaque endpoint ne fait que (1) extraire le buyerId du jeton,
// (2) construire une commande/requête, (3) la déléguer à MediatR. Aucune logique métier ici —
// elle vit dans les handlers (Application/) et le domaine. C'est la traduction « HTTP -> CQRS ».
//
// Sécurité — anti-IDOR (Insecure Direct Object Reference) : on n'accorde JAMAIS confiance au
// buyerId fourni par le client (paramètre d'URL ou corps). On le dérive TOUJOURS du jeton JWT,
// sinon un utilisateur pourrait lire/modifier les commandes d'un autre en changeant l'Id.
public static class OrderingApi
{
    public static RouteGroupBuilder MapOrderingApi(this IEndpointRouteBuilder app)
    {
        // MapGroup : préfixe commun à tous les endpoints. RequireAuthorization() est appliqué
        // sur le groupe dans Program.cs -> tous ces endpoints exigent un jeton valide.
        var group = app.MapGroup("/api/orders");

        // GET /api/orders/{buyerId}
        // Le paramètre d'URL est conservé pour ne pas casser le front mais IGNORÉ :
        // le buyerId est TOUJOURS dérivé du jeton JWT (anti-IDOR).
        group.MapGet("/{buyerId}", async (string buyerId, ClaimsPrincipal user, IMediator mediator) =>
        {
            var tokenBuyerId = GetBuyerId(user);
            if (string.IsNullOrEmpty(tokenBuyerId))
                return Results.Unauthorized();

            var orders = await mediator.Send(new GetOrdersQuery(tokenBuyerId));
            return Results.Ok(orders);
        });

        // POST /api/orders
        // Le BuyerId fourni dans le body est IGNORÉ : il est dérivé du jeton JWT (anti-IDOR).
        group.MapPost("/", async (CreateOrderCommand command, ClaimsPrincipal user, IMediator mediator) =>
        {
            var tokenBuyerId = GetBuyerId(user);
            if (string.IsNullOrEmpty(tokenBuyerId))
                return Results.Unauthorized();

            var orderId = await mediator.Send(command with { BuyerId = tokenBuyerId });
            return Results.Created($"/api/orders/{orderId}", new { orderId });
        });

        // PUT /api/orders/{id}/await-validation
        // Le buyerId est dérivé du jeton JWT : le handler vérifie la propriété (anti-IDOR).
        group.MapPut("/{id:int}/await-validation", async (int id, ClaimsPrincipal user, IMediator mediator) =>
        {
            var tokenBuyerId = GetBuyerId(user);
            if (string.IsNullOrEmpty(tokenBuyerId))
                return Results.Unauthorized();

            // KeyNotFoundException (ordre inexistant OU n'appartenant pas à l'appelant, cf.
            // anti-IDOR dans le handler) -> 404 propre, plutôt qu'une exception non traduite
            // remontant en 500. On ne révèle pas la distinction « inexistant / pas à vous »
            // (même 404) pour ne pas fuiter l'existence des commandes d'autrui.
            try
            {
                await mediator.Send(new SetAwaitingValidationCommand(id, tokenBuyerId));
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // PUT /api/orders/{id}/ship
        // Le buyerId est dérivé du jeton JWT : le handler vérifie la propriété (anti-IDOR).
        group.MapPut("/{id:int}/ship", async (int id, ClaimsPrincipal user, IMediator mediator) =>
        {
            var tokenBuyerId = GetBuyerId(user);
            if (string.IsNullOrEmpty(tokenBuyerId))
                return Results.Unauthorized();

            // Ordre inexistant ou pas à l'appelant -> 404 (voir await-validation ci-dessus).
            try
            {
                await mediator.Send(new ShipOrderCommand(id, tokenBuyerId));
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // PUT /api/orders/{id}/cancel
        // Le buyerId est dérivé du jeton JWT : le handler vérifie la propriété (anti-IDOR).
        group.MapPut("/{id:int}/cancel", async (int id, ClaimsPrincipal user, IMediator mediator) =>
        {
            var tokenBuyerId = GetBuyerId(user);
            if (string.IsNullOrEmpty(tokenBuyerId))
                return Results.Unauthorized();

            // Ordre inexistant ou pas à l'appelant -> 404 (voir await-validation ci-dessus).
            try
            {
                await mediator.Send(new CancelOrderCommand(id, tokenBuyerId));
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        return group;
    }

    // Dérive le buyerId du jeton : claim "sub" puis repli sur NameIdentifier.
    private static string? GetBuyerId(ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
}
