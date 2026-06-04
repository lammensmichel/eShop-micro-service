using FluentValidation;

namespace Ordering.API.Application.Commands;

// VALIDATEUR FluentValidation de la commande de création. Il vérifie la FORME de l'entrée
// (champs requis, quantités positives...) AVANT que la commande n'atteigne le domaine.
// À ne pas confondre avec les INVARIANTS du domaine (dans le constructeur d'Order) : la
// validation applicative protège la frontière et renvoie de jolis messages 400 au client ;
// les invariants, eux, garantissent qu'un agrégat ne peut JAMAIS exister dans un état
// incohérent, même si quelqu'un contourne la couche application. Les deux se complètent.
// Le validateur est branché automatiquement dans le pipeline MediatR par ValidationBehavior.
//
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
