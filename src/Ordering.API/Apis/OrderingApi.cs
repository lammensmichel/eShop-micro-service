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
        group.MapGet("/{buyerId}", async (string buyerId, IMediator mediator) =>
        {
            var orders = await mediator.Send(new GetOrdersQuery(buyerId));
            return Results.Ok(orders);
        });

        // POST /api/orders
        group.MapPost("/", async (CreateOrderCommand command, IMediator mediator) =>
        {
            var orderId = await mediator.Send(command);
            return Results.Created($"/api/orders/{orderId}", new { orderId });
        });

        return group;
    }
}