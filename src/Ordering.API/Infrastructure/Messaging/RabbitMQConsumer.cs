using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using eShop.IntegrationEvents.Events;
using eShop.IntegrationEvents.Messaging;
using Ordering.API.Application.Commands;
using Ordering.API.Infrastructure.Idempotency;

namespace Ordering.API.Infrastructure.Messaging;

// POINT D'ENTRÉE ASYNCHRONE du service : consomme les integration events des autres services
// sur RabbitMQ et les transforme en commandes MediatR. C'est le pendant « réception » du
// pattern Outbox (côté émission : IntegrationEventLogPublisher).
//
// BackgroundService = service hébergé tournant en tâche de fond pour toute la durée de vie
// de l'application (ExecuteAsync est lancé au démarrage). Ici il ouvre une connexion au
// broker et écoute en continu.
//
// Vue d'ensemble du traitement d'un message (les garanties clés, détaillées plus bas) :
//   1) une seule queue ("ordering-events") liée à PLUSIEURS routing keys de l'exchange direct
//      partagé ; le dispatch se fait selon ea.RoutingKey (BuildHandler) ;
//   2) IDEMPOTENCE : on ignore un event déjà traité (table ProcessedIntegrationEvents) — le
//      bus garantit « au moins une fois », donc un même message peut être re-livré ;
//   3) TRANSACTION : changement métier + clé d'idempotence + entrées outbox sont committés
//      ensemble (tout ou rien) ;
//   4) ACK MANUEL : on n'acquitte (BasicAck) qu'APRÈS commit réussi ; en cas d'échec on
//      réessaie un nombre borné de fois, puis on route le message « poison » vers une DLQ
//      (Dead-Letter Queue) pour ne pas bloquer la file.
// Détails de chaque garantie en commentaire à l'endroit concerné.
public class RabbitMQConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitMQConsumer> _logger;
    private readonly string _connectionString;
    private IConnection? _connection;
    private IChannel? _channel;

    private const string ExchangeName = "eshop_event_bus";

    // Queue unique d'Ordering liée à toutes les routing keys consommées.
    private const string QueueName = "ordering-events";

    // Routing keys consommées par Ordering : le checkout (création de commande) et les
    // étapes de la saga (période de grâce, paiement réussi/échoué).
    private const string BasketCheckoutRoutingKey = "basket-checkout";
    private const string GracePeriodConfirmedRoutingKey = "ordering-grace-period-confirmed";
    private const string PaymentSucceededRoutingKey = "payment-succeeded";
    private const string PaymentFailedRoutingKey = "payment-failed";

    private static readonly string[] ConsumedRoutingKeys =
    [
        BasketCheckoutRoutingKey,
        GracePeriodConfirmedRoutingKey,
        PaymentSucceededRoutingKey,
        PaymentFailedRoutingKey
    ];

    // Dead-letter : exchange + queue dédiés pour isoler les messages "poison".
    private const string DeadLetterExchangeName = "eshop_event_bus_dlx";
    private const string DeadLetterQueueName = "ordering-events-dlq";
    private const string DeadLetterRoutingKey = "ordering-events-dead";

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

        // Liaison de la queue unique sur TOUTES les routing keys consommées.
        foreach (var routingKey in ConsumedRoutingKeys)
        {
            await _channel.QueueBindAsync(
                queue: QueueName,
                exchange: ExchangeName,
                routingKey: routingKey,
                cancellationToken: stoppingToken);
        }

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
        var routingKey = ea.RoutingKey;
        var body = ea.Body.ToArray();
        var json = Encoding.UTF8.GetString(body);

        if (!ConsumedRoutingKeys.Contains(routingKey))
        {
            // Routing key inattendue : échec déterministe -> dead-letter immédiat.
            _logger.LogWarning("Routing key {RoutingKey} inattendue, envoi en dead-letter", routingKey);
            await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
            return;
        }

        // Désérialise l'event vers son type concret selon la routing key, et extrait son Id
        // (clé d'idempotence). En cas d'échec déterministe (illisible/null) -> dead-letter.
        Guid eventId;
        Func<OrderingDbContext, IMediator, Task> process;
        try
        {
            (eventId, process) = BuildHandler(routingKey, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message {RoutingKey} illisible, envoi en dead-letter", routingKey);
            await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
            return;
        }

        _logger.LogInformation(
            "Received event {EventId} on routing key {RoutingKey}",
            eventId, routingKey);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            // Idempotence : si l'événement a déjà été traité, on acquitte sans rejouer.
            var alreadyProcessed = await dbContext.ProcessedIntegrationEvents
                .AnyAsync(p => p.EventId == eventId);

            if (alreadyProcessed)
            {
                _logger.LogInformation(
                    "Event {EventId} déjà traité, message ignoré (idempotence)",
                    eventId);
                await channel.BasicAckAsync(ea.DeliveryTag, false);
                return;
            }

            await ProcessEventAsync(dbContext, mediator, eventId, process);

            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Course d'idempotence : une livraison concurrente a inséré la même clé
            // (violation d'unicité Postgres 23505). L'événement est donc déjà traité ->
            // on acquitte de façon idempotente plutôt que de requeue inutilement.
            _logger.LogInformation(
                "Event {EventId} déjà traité (violation d'unicité concurrente), ack idempotent",
                eventId);
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur de traitement de l'event {EventId} ({RoutingKey})", eventId, routingKey);

            // Stratégie anti poison-message : on compte les tentatives via un en-tête
            // applicatif (x-retry-count) republié à chaque réessai. Sous le seuil, on
            // republie le message (compteur +1) puis on acquitte l'original. Au seuil,
            // on rejette sans requeue (requeue:false) pour router vers la DLX/DLQ.
            var retryCount = GetRetryCount(ea.BasicProperties);
            if (retryCount >= MaxRetries)
            {
                _logger.LogWarning(
                    "Event {EventId} dépasse {MaxRetries} tentatives, envoi en dead-letter",
                    eventId, MaxRetries);
                await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
            }
            else
            {
                await RepublishWithRetryAsync(channel, ea, retryCount + 1);
                await channel.BasicAckAsync(ea.DeliveryTag, false);
            }
        }
    }

    // Désérialise vers le bon type d'event selon la routing key et retourne l'Id
    // (idempotence) + l'action de traitement (commande/transition via MediatR).
    private static (Guid EventId, Func<OrderingDbContext, IMediator, Task> Process) BuildHandler(
        string routingKey, string json)
    {
        switch (routingKey)
        {
            case BasketCheckoutRoutingKey:
            {
                var evt = JsonSerializer.Deserialize<BasketCheckoutEvent>(json)
                    ?? throw new InvalidOperationException("BasketCheckoutEvent null");
                return (evt.Id, (db, mediator) => HandleCheckoutAsync(mediator, evt));
            }
            case GracePeriodConfirmedRoutingKey:
            {
                var evt = JsonSerializer.Deserialize<GracePeriodConfirmedIntegrationEvent>(json)
                    ?? throw new InvalidOperationException("GracePeriodConfirmedIntegrationEvent null");
                return (evt.Id, (db, mediator) =>
                    mediator.Send(new ConfirmGracePeriodCommand(evt.OrderId, evt.BuyerId)));
            }
            case PaymentSucceededRoutingKey:
            {
                var evt = JsonSerializer.Deserialize<OrderPaymentSucceededIntegrationEvent>(json)
                    ?? throw new InvalidOperationException("OrderPaymentSucceededIntegrationEvent null");
                return (evt.Id, (db, mediator) =>
                    mediator.Send(new ConfirmOrderPaymentCommand(evt.OrderId, evt.BuyerId)));
            }
            case PaymentFailedRoutingKey:
            {
                var evt = JsonSerializer.Deserialize<OrderPaymentFailedIntegrationEvent>(json)
                    ?? throw new InvalidOperationException("OrderPaymentFailedIntegrationEvent null");
                return (evt.Id, (db, mediator) =>
                    mediator.Send(new CancelOrderPaymentCommand(evt.OrderId, evt.BuyerId)));
            }
            default:
                throw new InvalidOperationException($"Routing key non gérée : {routingKey}");
        }
    }

    // Traitement du checkout (création de commande) -> CreateOrderCommand.
    private static async Task HandleCheckoutAsync(IMediator mediator, BasketCheckoutEvent checkoutEvent)
    {
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

        await mediator.Send(command);
    }

    // Republie le message sur l'exchange principal en incrémentant l'en-tête de tentatives,
    // en conservant la routing key d'origine (republication sur la même queue).
    private async Task RepublishWithRetryAsync(IChannel channel, BasicDeliverEventArgs ea, int retryCount)
    {
        var headers = ea.BasicProperties.Headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(ea.BasicProperties.Headers);
        headers[RetryCountHeader] = retryCount;

        // On RÉ-ALIGNE les BasicProperties sur la publication d'origine (RabbitMQPublisher) :
        // un message republié pour retry doit être indistinguable d'un message frais. Omettre
        // ContentType ou la persistance dégraderait silencieusement le réessai :
        //   - ContentType="application/json" : conserve le contrat de format. Un consommateur
        //     (ou un outillage) qui s'y fie ne doit pas voir le type disparaître au 2e essai.
        //   - DeliveryMode persistant : le message republié doit, lui aussi, survivre à un
        //     redémarrage du broker (sinon un retry pourrait être perdu là où l'original ne
        //     l'aurait pas été).
        var properties = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json",
            Headers = headers
        };

        // mandatory:true comme à l'émission : la queue est liée à cette routing key, donc le
        // message DOIT être routable ; un échec de routage est alors signalé bruyamment plutôt
        // qu'avalé en silence (cohérence avec RabbitMQPublisher).
        await channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: ea.RoutingKey,
            mandatory: true,
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
    // que le changement métier. Le traitement (process) peut envoyer une commande ou une
    // transition via MediatR ; les SaveChangesAsync qu'elle déclenche dispatchent les
    // domain events -> handlers qui enfilent les events d'intégration sortants dans l'outbox.
    private async Task ProcessEventAsync(
        OrderingDbContext dbContext,
        IMediator mediator,
        Guid eventId,
        Func<OrderingDbContext, IMediator, Task> process)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            await process(dbContext, mediator);

            dbContext.ProcessedIntegrationEvents.Add(new ProcessedIntegrationEvent
            {
                EventId = eventId,
                ProcessedOn = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.LogInformation("Event {EventId} traité avec succès", eventId);
        });
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
