using Microsoft.Extensions.DependencyInjection;

namespace eShop.IntegrationEvents.Messaging;

// =============================================================================
// FICHIER : EventBusExtensions.cs
// RÔLE    : "câblage" du bus dans le conteneur d'injection de dépendances (DI).
//           Une méthode d'extension AddRabbitMQEventBus(...) que chaque service
//           appelle dans son Program.cs.
// CONCEPT : ENREGISTREMENT DI + DURÉE DE VIE SINGLETON.
//
//   - "Singleton" = une SEULE instance pour toute la vie de l'application. C'est
//     le bon choix ici car RabbitMQPublisher détient une connexion TCP partagée :
//     on veut la créer une fois et la réutiliser, pas la rouvrir à chaque requête.
//   - On enregistre la MÊME instance sous deux faces : l'interface IEventBus (ce
//     dont dépend le code métier) et le type concret RabbitMQPublisher (dont a
//     besoin le health check pour appeler CheckConnectionAsync). Le second
//     enregistrement délègue au premier (factory sp => GetRequiredService<...>),
//     donc une et une seule connexion partagée.
//
// À LIRE :
//   - AVANT : RabbitMQPublisher.cs (ce qu'on enregistre).
//   - APRÈS : Basket.API/Program.cs (l'appelant) et
//             Basket.API/Messaging/RabbitMQHealthCheck.cs (consommateur du type concret).
// =============================================================================

/// <summary>
/// Extensions DI du bus d'événements partagé.
/// </summary>
public static class EventBusExtensions
{
    /// <summary>
    /// Enregistre le bus RabbitMQ en singleton derrière <see cref="IEventBus"/>.
    /// La même instance concrète <see cref="RabbitMQPublisher"/> est aussi résolvable
    /// directement (utile, par exemple, pour un health check qui appelle
    /// <see cref="RabbitMQPublisher.CheckConnectionAsync"/>).
    /// </summary>
    public static IServiceCollection AddRabbitMQEventBus(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton(new RabbitMQPublisher(connectionString));
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<RabbitMQPublisher>());
        return services;
    }
}
