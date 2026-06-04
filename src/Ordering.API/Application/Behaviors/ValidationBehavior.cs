using FluentValidation;
using MediatR;

namespace Ordering.API.Application.Behaviors;

// PIPELINE BEHAVIOR MediatR (IPipelineBehavior). Un « behavior » est un intercepteur qui
// entoure CHAQUE handler — l'équivalent d'un middleware ASP.NET, mais pour les requêtes
// MediatR. MediatR les enchaîne autour du handler : chacun peut agir avant/après en
// appelant `next()`. C'est l'endroit idéal pour des préoccupations transverses (validation,
// logs, transactions...) sans polluer les handlers.
//
// Ce behavior-ci exécute tous les validateurs FluentValidation enregistrés pour la requête
// AVANT d'appeler le handler. Si un validateur échoue, il lève une ValidationException et
// `next()` n'est jamais appelé -> le handler (et donc le domaine) n'est pas touché.
// L'exception est ensuite traduite en réponse HTTP 400 par un middleware dans Program.cs.
// Enregistré ouvert (typeof(ValidationBehavior<,>)) dans Program.cs, il s'applique à toutes
// les commandes/requêtes.
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);

            var failures = (await Task.WhenAll(
                    _validators.Select(v => v.ValidateAsync(context, cancellationToken))))
                .SelectMany(r => r.Errors)
                .Where(f => f is not null)
                .ToList();

            if (failures.Count != 0)
                throw new ValidationException(failures);
        }

        return await next(cancellationToken);
    }
}
