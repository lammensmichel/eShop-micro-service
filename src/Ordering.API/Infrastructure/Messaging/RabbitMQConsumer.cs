using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using eShop.IntegrationEvents.Events;
using Ordering.API.Application.Commands;
using Ordering.API.Infrastructure.Idempotency;

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

    // Dead-letter : exchange + queue dédiés pour isoler les messages "poison".
    private const string DeadLetterExchangeName = "eshop_event_bus_dlx";
    private const string DeadLetterQueueName = "ordering-basket-checkout-dlq";
    private const string DeadLetterRoutingKey = "basket-checkout-dead";

    // Nombre maximal de tentatives avant de router vers la DLQ.
    private const int MaxRetries = 3;

    // En-tête applicatif comptant les tentatives. On NE se repose PAS sur x-death :
    // un nack(requeue:true) n'incrémente pas x-death, donc le compteur resterait à 0
    // et un message en échec déterministe boucleait à l'infini (poison message).
    private const string RetryCountHeader = "x-retry-count";

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

        // prefetch=1 : un seul message non acquitté à la fois -> pas de traitement
        // concurrent du même EventId, ce qui simplifie la garantie d'idempotence.
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        await _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            cancellationToken: stoppingToken);

        // Déclaration de la dead-letter exchange + queue.
        await _channel.ExchangeDeclareAsync(
            exchange: DeadLetterExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            queue: DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(
            queue: DeadLetterQueueName,
            exchange: DeadLetterExchangeName,
            routingKey: DeadLetterRoutingKey,
            cancellationToken: stoppingToken);

        // La queue principale route les messages rejetés (nack requeue:false) vers la DLX.
        var queueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = DeadLetterExchangeName,
            ["x-dead-letter-routing-key"] = DeadLetterRoutingKey
        };

        await _channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments,
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(
            queue: QueueName,
            exchange: ExchangeName,
            routingKey: RoutingKey,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var channel = _channel!;
        var body = ea.Body.ToArray();
        var json = Encoding.UTF8.GetString(body);

        BasketCheckoutEvent? checkoutEvent;
        try
        {
            checkoutEvent = JsonSerializer.Deserialize<BasketCheckoutEvent>(json);
        }
        catch (Exception ex)
        {
            // Message non désérialisable : échec déterministe -> dead-letter immédiat.
            _logger.LogError(ex, "BasketCheckoutEvent illisible, envoi en dead-letter");
            await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
            return;
        }

        if (checkoutEvent is null)
        {
            _logger.LogWarning("BasketCheckoutEvent null, envoi en dead-letter");
            await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
            return;
        }

        _logger.LogInformation(
            "Received BasketCheckoutEvent {EventId} for buyer {BuyerId}",
            checkoutEvent.Id,
            checkoutEvent.BuyerId);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            // Idempotence : si l'événement a déjà été traité, on acquitte sans rejouer.
            var alreadyProcessed = await dbContext.ProcessedIntegrationEvents
                .AnyAsync(p => p.EventId == checkoutEvent.Id);

            if (alreadyProcessed)
            {
                _logger.LogInformation(
                    "BasketCheckoutEvent {EventId} déjà traité, message ignoré (idempotence)",
                    checkoutEvent.Id);
                await channel.BasicAckAsync(ea.DeliveryTag, false);
                return;
            }

            await ProcessEventAsync(dbContext, mediator, checkoutEvent);

            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Course d'idempotence : une livraison concurrente a inséré la même clé
            // (violation d'unicité Postgres 23505). L'événement est donc déjà traité ->
            // on acquitte de façon idempotente plutôt que de requeue inutilement.
            _logger.LogInformation(
                "BasketCheckoutEvent {EventId} déjà traité (violation d'unicité concurrente), ack idempotent",
                checkoutEvent.Id);
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur de traitement du BasketCheckoutEvent {EventId}", checkoutEvent.Id);

            // Stratégie anti poison-message : on compte les tentatives via un en-tête
            // applicatif (x-retry-count) republié à chaque réessai. Sous le seuil, on
            // republie le message (compteur +1) puis on acquitte l'original. Au seuil,
            // on rejette sans requeue (requeue:false) pour router vers la DLX/DLQ.
            var retryCount = GetRetryCount(ea.BasicProperties);
            if (retryCount >= MaxRetries)
            {
                _logger.LogWarning(
                    "BasketCheckoutEvent {EventId} dépasse {MaxRetries} tentatives, envoi en dead-letter",
                    checkoutEvent.Id, MaxRetries);
                await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
            }
            else
            {
                await RepublishWithRetryAsync(channel, ea, retryCount + 1);
                await channel.BasicAckAsync(ea.DeliveryTag, false);
            }
        }
    }

    // Republie le message sur l'exchange principal en incrémentant l'en-tête de tentatives.
    private async Task RepublishWithRetryAsync(IChannel channel, BasicDeliverEventArgs ea, int retryCount)
    {
        var headers = ea.BasicProperties.Headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(ea.BasicProperties.Headers);
        headers[RetryCountHeader] = retryCount;

        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = headers
        };

        await channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: RoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: ea.Body);
    }

    // Lit le compteur de tentatives applicatif (x-retry-count). Absent = 0.
    private static int GetRetryCount(IReadOnlyBasicProperties? properties)
    {
        if (properties?.Headers is null)
            return 0;

        if (!properties.Headers.TryGetValue(RetryCountHeader, out var raw) || raw is null)
            return 0;

        return raw switch
        {
            int i => i,
            long l => (int)l,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 0
        };
    }

    // Détecte une violation de contrainte d'unicité Postgres (SQLSTATE 23505).
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    // Traite l'événement et enregistre la clé d'idempotence dans la MÊME transaction
    // que la création de la commande.
    private async Task ProcessEventAsync(
        OrderingDbContext dbContext,
        IMediator mediator,
        BasketCheckoutEvent checkoutEvent)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();

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

            dbContext.ProcessedIntegrationEvents.Add(new ProcessedIntegrationEvent
            {
                EventId = checkoutEvent.Id,
                ProcessedOn = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.LogInformation("Order {OrderId} created successfully", orderId);
        });
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
