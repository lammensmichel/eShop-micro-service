namespace Basket.API.Messaging;

public interface IEventPublisher
{
    Task PublishAsync<T>(T message, string routingKey);
}