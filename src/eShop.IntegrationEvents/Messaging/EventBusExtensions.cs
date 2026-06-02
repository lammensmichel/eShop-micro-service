using Microsoft.Extensions.DependencyInjection;

namespace eShop.IntegrationEvents.Messaging;

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
