using MediatR;

namespace Ordering.API.Application.Commands;

// Commandes pilotant le cycle de vie de l'agrégat Order (point 9).
// BuyerId est dérivé du jeton JWT côté API (anti-IDOR) : le handler vérifie
// que la commande appartient bien à l'appelant.
public record SetAwaitingValidationCommand(int OrderId, string BuyerId) : IRequest<Unit>;

public record ShipOrderCommand(int OrderId, string BuyerId) : IRequest<Unit>;

public record CancelOrderCommand(int OrderId, string BuyerId) : IRequest<Unit>;
