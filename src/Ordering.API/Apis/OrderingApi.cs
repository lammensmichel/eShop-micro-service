using System.Security.Claims;
using MediatR;
using Ordering.API.Application.Commands;
using Ordering.API.Application.Queries;

namespace Ordering.API.Apis;

public static class OrderingApi
{
    public static RouteGroupBuilder MapOrderingApi(this IEndpointRouteBuilder app)
    {
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

            await mediator.Send(new SetAwaitingValidationCommand(id, tokenBuyerId));
            return Results.NoContent();
        });

        // PUT /api/orders/{id}/ship
        // Le buyerId est dérivé du jeton JWT : le handler vérifie la propriété (anti-IDOR).
        group.MapPut("/{id:int}/ship", async (int id, ClaimsPrincipal user, IMediator mediator) =>
        {
            var tokenBuyerId = GetBuyerId(user);
            if (string.IsNullOrEmpty(tokenBuyerId))
                return Results.Unauthorized();

            await mediator.Send(new ShipOrderCommand(id, tokenBuyerId));
            return Results.NoContent();
        });

        // PUT /api/orders/{id}/cancel
        // Le buyerId est dérivé du jeton JWT : le handler vérifie la propriété (anti-IDOR).
        group.MapPut("/{id:int}/cancel", async (int id, ClaimsPrincipal user, IMediator mediator) =>
        {
            var tokenBuyerId = GetBuyerId(user);
            if (string.IsNullOrEmpty(tokenBuyerId))
                return Results.Unauthorized();

            await mediator.Send(new CancelOrderCommand(id, tokenBuyerId));
            return Results.NoContent();
        });

        return group;
    }

    // Dérive le buyerId du jeton : claim "sub" puis repli sur NameIdentifier.
    private static string? GetBuyerId(ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
}
