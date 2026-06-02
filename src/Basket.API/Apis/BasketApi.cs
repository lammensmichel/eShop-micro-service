using Basket.API.Messaging;
using Basket.API.Models;
using Basket.API.Repositories;
using eShop.IntegrationEvents.Events;

namespace Basket.API.Apis;

public static class BasketApi
{
    public static RouteGroupBuilder MapBasketApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/basket");

        group.MapGet("/{buyerId}", async (string buyerId, IBasketRepository repository) =>
        {
            var basket = await repository.GetBasketAsync(buyerId);
            return basket is null
                ? Results.Ok(new CustomerBasket { BuyerId = buyerId })
                : Results.Ok(basket);
        });

        group.MapPost("/", async (CustomerBasket basket, IBasketRepository repository) =>
        {
            var updated = await repository.UpdateBasketAsync(basket);
            return Results.Ok(updated);
        });

        group.MapPost("/checkout", async (
            BasketCheckout checkout,
            IBasketRepository repository,
            IEventPublisher publisher) =>
        {
            var basket = await repository.GetBasketAsync(checkout.BuyerId);
            if (basket is null) return Results.NotFound();

            var eventMessage = new BasketCheckoutEvent
            {
                BuyerId = checkout.BuyerId,
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

            await publisher.PublishAsync(eventMessage, "basket-checkout");
            await repository.DeleteBasketAsync(checkout.BuyerId);

            return Results.Accepted();
        });

        group.MapDelete("/{buyerId}", async (string buyerId, IBasketRepository repository) =>
        {
            await repository.DeleteBasketAsync(buyerId);
            return Results.NoContent();
        });

        return group;
    }
}