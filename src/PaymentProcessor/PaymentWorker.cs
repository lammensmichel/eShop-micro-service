using System.Text;
using System.Text.Json;
using eShop.IntegrationEvents.Events;
using eShop.IntegrationEvents.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PaymentProcessor;

// Worker « paiement simulé » : consomme OrderStockConfirmedIntegrationEvent
// (routing key "ordering-order-stock-confirmed"), simule un paiement, puis publie soit
// OrderPaymentSucceededIntegrationEvent (routing "payment-succeeded") soit
// OrderPaymentFailedIntegrationEvent (routing "payment-failed", avec Reason).
//
// Comportement par défaut : succès. Échec forcé si "Payment:AlwaysFail" = true.
//
// Pattern de connexion/canal/consommation calqué sur le RabbitMQConsumer d'Ordering :
// exchange direct durable partagé "eshop_event_bus", queue durable propre au worker,
// liaison sur SA routing key, autoAck:false, prefetch=1, ack après publication réussie,
// nack(requeue:false) si le message est illisible (échec déterministe).
public class PaymentWorker : BackgroundService
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<PaymentWorker> _logger;
    private readonly string _connectionString;
    private readonly bool _alwaysFail;
    private IConnection? _connection;
    private IChannel? _channel;

    private const string ExchangeName = "eshop_event_bus";

    // Queue durable propre à ce worker.
    private const string QueueName = "paymentprocessor-events";

    // Routing key consommée : stock confirmé.
    private const string StockConfirmedRoutingKey = "ordering-order-stock-confirmed";

    // Routing keys publiées : résultat du paiement.
    private const string PaymentSucceededRoutingKey = "payment-succeeded";
    private const string PaymentFailedRoutingKey = "payment-failed";

    public PaymentWorker(
        IEventBus eventBus,
        ILogger<PaymentWorker> logger,
        IConfiguration configuration)
    {
        _eventBus = eventBus;
        _logger = logger;
        _connectionString = configuration.GetConnectionString("rabbitmq")!;

        // Échec forcé du paiement si "Payment:AlwaysFail" = true (défaut : false -> succès).
        _alwaysFail = configuration.GetValue("Payment:AlwaysFail", false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory { Uri = new Uri(_connectionString) };
        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // prefetch=1 : un seul message non acquitté à la fois.
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

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
            routingKey: StockConfirmedRoutingKey,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation(
            "PaymentProcessor démarré : écoute '{RoutingKey}', AlwaysFail={AlwaysFail}",
            StockConfirmedRoutingKey, _alwaysFail);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var channel = _channel!;
        var json = Encoding.UTF8.GetString(ea.Body.ToArray());

        OrderStockConfirmedIntegrationEvent evt;
        try
        {
            evt = JsonSerializer.Deserialize<OrderStockConfirmedIntegrationEvent>(json)
                ?? throw new InvalidOperationException("OrderStockConfirmedIntegrationEvent null");
        }
        catch (Exception ex)
        {
            // Message illisible : échec déterministe -> nack sans requeue (rejet définitif).
            _logger.LogError(ex, "Message '{RoutingKey}' illisible, rejet (nack)", ea.RoutingKey);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        _logger.LogInformation(
            "Stock confirmé pour la commande {OrderId} (event {EventId}) : simulation du paiement",
            evt.OrderId, evt.Id);

        try
        {
            // Simulation du paiement : succès par défaut, échec si Payment:AlwaysFail=true.
            if (_alwaysFail)
            {
                var failed = new OrderPaymentFailedIntegrationEvent
                {
                    OrderId = evt.OrderId,
                    BuyerId = evt.BuyerId,
                    Reason = "Paiement refusé (simulation : Payment:AlwaysFail=true)"
                };

                await _eventBus.PublishAsync(failed, PaymentFailedRoutingKey);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);

                _logger.LogInformation(
                    "Paiement ÉCHOUÉ pour la commande {OrderId} : '{RoutingKey}' publié",
                    evt.OrderId, PaymentFailedRoutingKey);
            }
            else
            {
                var succeeded = new OrderPaymentSucceededIntegrationEvent
                {
                    OrderId = evt.OrderId,
                    BuyerId = evt.BuyerId
                };

                await _eventBus.PublishAsync(succeeded, PaymentSucceededRoutingKey);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);

                _logger.LogInformation(
                    "Paiement RÉUSSI pour la commande {OrderId} : '{RoutingKey}' publié",
                    evt.OrderId, PaymentSucceededRoutingKey);
            }
        }
        catch (Exception ex)
        {
            // Erreur transitoire (ex. publication) : requeue pour réessayer.
            _logger.LogError(ex, "Échec du traitement de la commande {OrderId}, requeue", evt.OrderId);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
