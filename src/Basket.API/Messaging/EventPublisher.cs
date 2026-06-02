namespace Basket.API.Messaging;

public class EventPublisher : IEventPublisher
{
    private readonly RabbitMQPublisher _publisher;

    public EventPublisher(RabbitMQPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task PublishAsync<T>(T message, string routingKey)
        => _publisher.PublishAsync(message, routingKey);
}