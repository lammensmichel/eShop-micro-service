using System.Text;
using System.Text.Json;
using MediatR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using eShop.IntegrationEvents.Events;
using Ordering.API.Application.Commands;

namespace Ordering.API.Infrastructure.Messaging;

public class RabbitMQConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitMQConsumer> _logger;
    private readonly string _connectionString;
    private IConnection? _connection;
    private IChannel? _channel;
    private const string ExchangeName = "eshop_event_bus";
    private const string QueueName = "ordering-basket-checkout";
    private const string RoutingKey = "basket-checkout";

    public RabbitMQConsumer(
        IServiceProvider serviceProvider,
        ILogger<RabbitMQConsumer> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _connectionString = configuration.GetConnectionString("rabbitmq")!;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory { Uri = new Uri(_connectionString) };
        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(
            queue: QueueName,
            exchange: ExchangeName,
            routingKey: RoutingKey,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var body = ea.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);

            try
            {
                var checkoutEvent = JsonSerializer.Deserialize<BasketCheckoutEvent>(json);
                if (checkoutEvent is null) return;

                _logger.LogInformation(
                    "Received BasketCheckoutEvent for buyer {BuyerId}", 
                    checkoutEvent.BuyerId);

                using var scope = _serviceProvider.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                var command = new CreateOrderCommand
                {
                    BuyerId = checkoutEvent.BuyerId,
                    City = checkoutEvent.City,
                    Street = checkoutEvent.Street,
                    Country = checkoutEvent.Country,
                    ZipCode = checkoutEvent.ZipCode,
                    Items = checkoutEvent.Items.Select(i => new CreateOrderCommandItem
                    {
                        ProductId = i.ProductId,
                        ProductName = i.ProductName,
                        UnitPrice = i.UnitPrice,
                        Quantity = i.Quantity
                    }).ToList()
                };

                var orderId = await mediator.Send(command);
                _logger.LogInformation("Order {OrderId} created successfully", orderId);

                await _channel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing BasketCheckoutEvent");
                await _channel.BasicNackAsync(ea.DeliveryTag, false, true);
            }
        };

        await _channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}