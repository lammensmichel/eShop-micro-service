using System.Text;
using System.Text.Json;
using eShop.IntegrationEvents.Events;
using eShop.IntegrationEvents.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PaymentProcessor;

// ============================================================================
// PaymentWorker — l'étape « paiement » de la saga de commande.
// ----------------------------------------------------------------------------
// RÔLE : une fois le stock confirmé par Ordering, ce worker simule le paiement
// et émet le résultat, qui fera AVANCER ou COMPENSER la commande. Concrètement :
//   1. CONSOMME  OrderStockConfirmedIntegrationEvent
//                (routing key "ordering-order-stock-confirmed") ;
//   2. SIMULE    un paiement (succès par défaut, échec si Payment:AlwaysFail=true) ;
//   3. PUBLIE    soit OrderPaymentSucceededIntegrationEvent (routing "payment-succeeded"),
//                soit OrderPaymentFailedIntegrationEvent    (routing "payment-failed",
//                                                             avec un Reason explicatif).
//
// CONCEPT ILLUSTRÉ — « branche » de la chorégraphie saga.
//   Dans une saga, certaines étapes ont DEUX issues possibles. Ici le paiement
//   réussit ou échoue : selon le cas, on émet deux événements DIFFÉRENTS sur deux
//   routing keys différentes. C'est Ordering, à l'autre bout, qui réagira soit en
//   confirmant la commande (succès), soit en lançant la COMPENSATION : annuler la
//   commande (échec). Le worker n'a aucune connaissance de cette suite — il se
//   contente d'émettre le fait « paiement réussi/échoué » (découplage par événements).
//
// L'option Payment:AlwaysFail permet de tester FACILEMENT le chemin d'échec/
// compensation de la saga sans vrai prestataire de paiement.
//
// PLACE DANS L'ENSEMBLE :
//   Ordering -- ordering-order-stock-confirmed --> [CE WORKER] -- payment-succeeded --> Ordering
//                                                              \-- payment-failed ----> Ordering
//
// PATTERN RabbitMQ identique à GracePeriodWorker (voir ses commentaires détaillés) :
// exchange direct durable partagé "eshop_event_bus", queue durable propre au worker,
// liaison sur SA routing key, autoAck:false (ack manuel), prefetch=1, ack après
// publication réussie, nack(requeue:false) si message illisible (échec déterministe).
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

    // Dépendances injectées par le conteneur DI (configuré dans Program.cs) :
    // IEventBus (publier le résultat), ILogger (journalisation -> OpenTelemetry),
    // IConfiguration (chaîne de connexion + comportement de simulation).
    public PaymentWorker(
        IEventBus eventBus,
        ILogger<PaymentWorker> logger,
        IConfiguration configuration)
    {
        _eventBus = eventBus;
        _logger = logger;
        // Chaîne de connexion injectée par Aspire via WithReference(rabbitmq) — pas d'URL en dur.
        _connectionString = configuration.GetConnectionString("rabbitmq")!;

        // Échec forcé du paiement si "Payment:AlwaysFail" = true (défaut : false -> succès).
        // Sert à exercer le chemin de COMPENSATION de la saga (annulation de commande).
        _alwaysFail = configuration.GetValue("Payment:AlwaysFail", false);
    }

    // Boucle de fond : connexion RabbitMQ + déclaration de la topologie + abonnement,
    // puis attente jusqu'à l'arrêt. stoppingToken est déclenché à l'arrêt de l'app.
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

        // Consommation pilotée par événement : chaque message déclenche notre handler.
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false, // ack manuel : on acquitte seulement après publication du résultat
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation(
            "PaymentProcessor démarré : écoute '{RoutingKey}', AlwaysFail={AlwaysFail}",
            StockConfirmedRoutingKey, _alwaysFail);

        // On empêche ExecuteAsync de se terminer (sinon le worker s'arrêterait) :
        // attente « infinie » annulée par le stoppingToken au moment de l'arrêt.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    // Handler appelé par RabbitMQ pour chaque message livré. ea.DeliveryTag sert à
    // acquitter (ack) ou rejeter (nack) ; ea.Body porte le JSON de l'événement.
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
            // Dans les deux branches, on PUBLIE d'abord le résultat, PUIS on acquitte
            // (ack après publication réussie -> garantie at-least-once, voir plus bas).
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
            // Erreur TRANSITOIRE (ex. publication impossible) : requeue:true pour que le
            // broker redélivre le message et qu'on réessaie plus tard. À distinguer du
            // message illisible plus haut (échec déterministe -> requeue:false, rejet définitif).
            _logger.LogError(ex, "Échec du traitement de la commande {OrderId}, requeue", evt.OrderId);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }

    // Arrêt propre : libère canal puis connexion. Les messages non acquittés seront
    // redélivrés au prochain démarrage (le paiement sera donc rejoué -> idempotence).
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
