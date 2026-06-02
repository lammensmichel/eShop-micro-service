using System.Text;
using System.Text.Json;
using eShop.IntegrationEvents.Events;
using eShop.IntegrationEvents.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderProcessor;

// Worker « grace period » : consomme OrderStatusChangedToSubmittedIntegrationEvent
// (routing key "ordering-order-submitted"), attend une période de grâce configurable,
// puis publie GracePeriodConfirmedIntegrationEvent (routing "ordering-grace-period-confirmed").
//
// Pattern de connexion/canal/consommation calqué sur le RabbitMQConsumer d'Ordering :
// exchange direct durable partagé "eshop_event_bus", queue durable propre au worker,
// liaison sur SA routing key, autoAck:false, prefetch=1, ack après publication réussie,
// nack(requeue:false) si le message est illisible (échec déterministe).
public class GracePeriodWorker : BackgroundService
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<GracePeriodWorker> _logger;
    private readonly string _connectionString;
    private readonly TimeSpan _gracePeriod;
    private IConnection? _connection;
    private IChannel? _channel;
    private CancellationToken _stoppingToken;

    private const string ExchangeName = "eshop_event_bus";

    // Queue durable propre à ce worker.
    private const string QueueName = "orderprocessor-events";

    // Routing key consommée : commande venant d'être soumise.
    private const string SubmittedRoutingKey = "ordering-order-submitted";

    // Routing key publiée : grace period écoulée.
    private const string GracePeriodConfirmedRoutingKey = "ordering-grace-period-confirmed";

    public GracePeriodWorker(
        IEventBus eventBus,
        ILogger<GracePeriodWorker> logger,
        IConfiguration configuration)
    {
        _eventBus = eventBus;
        _logger = logger;
        _connectionString = configuration.GetConnectionString("rabbitmq")!;

        // Période de grâce configurable via "GracePeriod:Seconds" (défaut 10 s).
        var seconds = configuration.GetValue("GracePeriod:Seconds", 10);
        _gracePeriod = TimeSpan.FromSeconds(seconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        var factory = new ConnectionFactory { Uri = new Uri(_connectionString) };
        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // prefetch=1 : un seul message non acquitté à la fois. Ici c'est aussi un garde-fou
        // important car le handler attend la période de grâce avant d'acquitter : on évite
        // ainsi de bloquer plusieurs messages en parallèle sur le même canal.
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
            routingKey: SubmittedRoutingKey,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation(
            "OrderProcessor démarré : écoute '{RoutingKey}', période de grâce {Grace}s",
            SubmittedRoutingKey, _gracePeriod.TotalSeconds);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var channel = _channel!;
        var stoppingToken = _stoppingToken;
        var json = Encoding.UTF8.GetString(ea.Body.ToArray());

        OrderStatusChangedToSubmittedIntegrationEvent evt;
        try
        {
            evt = JsonSerializer.Deserialize<OrderStatusChangedToSubmittedIntegrationEvent>(json)
                ?? throw new InvalidOperationException("OrderStatusChangedToSubmittedIntegrationEvent null");
        }
        catch (Exception ex)
        {
            // Message illisible : échec déterministe -> nack sans requeue (rejet définitif).
            _logger.LogError(ex, "Message '{RoutingKey}' illisible, rejet (nack)", ea.RoutingKey);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        _logger.LogInformation(
            "Commande {OrderId} reçue (event {EventId}) : début de la période de grâce ({Grace}s)",
            evt.OrderId, evt.Id, _gracePeriod.TotalSeconds);

        try
        {
            // Attente non bloquante de la période de grâce.
            //
            // LIMITE (projet d'apprentissage) : l'échéance n'est PAS persistée. Si le worker
            // redémarre pendant l'attente, le message non acquitté sera redélivré et la grâce
            // repartira de zéro ; à l'inverse, l'attente en cours est simplement perdue.
            // Une vraie implémentation persisterait l'échéance (ex. table / scheduler) pour
            // reprendre exactement où elle en était.
            await Task.Delay(_gracePeriod, stoppingToken);

            var confirmed = new GracePeriodConfirmedIntegrationEvent
            {
                OrderId = evt.OrderId,
                BuyerId = evt.BuyerId
            };

            await _eventBus.PublishAsync(confirmed, GracePeriodConfirmedRoutingKey);

            // Ack seulement après publication réussie : si la publication échoue, le catch
            // ci-dessous requeue le message pour réessayer plus tard.
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);

            _logger.LogInformation(
                "Grace period écoulée pour la commande {OrderId} : '{RoutingKey}' publié",
                evt.OrderId, GracePeriodConfirmedRoutingKey);
        }
        catch (OperationCanceledException)
        {
            // Arrêt du worker pendant l'attente : on ne publie pas, on ne ré-acquitte pas.
            // Le message restant non acquitté sera redélivré au prochain démarrage.
            _logger.LogInformation(
                "Arrêt pendant la grace period de la commande {OrderId} : message non acquitté (sera redélivré)",
                evt.OrderId);
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
