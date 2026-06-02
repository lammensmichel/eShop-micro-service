using FluentValidation;

namespace Ordering.API.Application.Commands;

// Validation de la commande de création (point 6).
// Note : BuyerId est dérivé du jeton côté API avant l'envoi ; on le valide
// néanmoins pour garantir l'invariant en entrée du domaine.
public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(c => c.BuyerId).NotEmpty().WithMessage("Le BuyerId est obligatoire.");
        RuleFor(c => c.Items).NotEmpty().WithMessage("La commande doit contenir au moins un article.");

        RuleForEach(c => c.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Quantity).GreaterThan(0).WithMessage("La quantité doit être supérieure à 0.");
            item.RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0).WithMessage("Le prix unitaire doit être positif ou nul.");
        });
    }
}
