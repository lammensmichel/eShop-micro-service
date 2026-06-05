using System.Text;
using System.Text.Json;
using eShop.IntegrationEvents.Events;
using eShop.IntegrationEvents.Messaging;
using PaymentProcessor.Payment;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PaymentProcessor;

// ============================================================================
// PaymentWorker — l'étape « paiement » de la saga de commande.
// ----------------------------------------------------------------------------
// RÔLE : une fois le stock confirmé par Ordering, ce worker fait DÉBITER le
// paiement par une PASSERELLE (IPaymentGateway) et émet le résultat, qui fera
// AVANCER ou COMPENSER la commande. Concrètement :
//   1. CONSOMME  OrderStockConfirmedIntegrationEvent
//                (routing key "ordering-order-stock-confirmed") ;
//   2. CHARGE    le paiement via _gateway.ChargeAsync(PaymentRequest) — le worker
//                ne sait PAS si c'est une simulation ou un vrai prestataire ;
//   3. PUBLIE    soit OrderPaymentSucceededIntegrationEvent (routing "payment-succeeded",
//                avec le TransactionId renvoyé par la passerelle),
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
// ABSTRACTION DU PAIEMENT — IPaymentGateway.
//   Le worker délègue tout le « comment on paie » à une passerelle injectée. Par
//   défaut c'est SimulatedPaymentGateway (succès si le moyen de paiement est
//   valide, échec sinon, et échec forcé si Payment:AlwaysFail=true — utile pour
//   tester la compensation). Pour un vrai paiement, on n'échange QUE l'implémentation
//   enregistrée dans Program.cs : ce worker reste inchangé.
//
// ⚠️ PCI-DSS : l'événement consommé transporte ici un numéro de carte EN CLAIR,
//   UNIQUEMENT pour la simulation pédagogique. En PRODUCTION, ces données carte ne
//   doivent JAMAIS transiter en clair (ni sur le bus, ni dans nos logs) : on
//   utiliserait un JETON (tokenisation chez le prestataire au checkout).
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
    private readonly IPaymentGateway _gateway;
    private readonly ILogger<PaymentWorker> _logger;
    private readonly ConsumerState _consumerState;
    private readonly string _connectionString;
    private IConnection? _connection;
    private IChannel? _channel;

    // Tag du consommateur renvoyé par BasicConsumeAsync : nécessaire pour ANNULER
    // proprement la consommation (BasicCancelAsync) lors de l'arrêt gracieux.
    private string? _consumerTag;

    private const string ExchangeName = "eshop_event_bus";

    // Queue durable propre à ce worker.
    private const string QueueName = "paymentprocessor-events";

    // Routing key consommée : stock confirmé.
    private const string StockConfirmedRoutingKey = "ordering-order-stock-confirmed";

    // Routing keys publiées : résultat du paiement.
    private const string PaymentSucceededRoutingKey = "payment-succeeded";
    private const string PaymentFailedRoutingKey = "payment-failed";

    // Dépendances injectées par le conteneur DI (configuré dans Program.cs) :
    // IEventBus (publier le résultat), IPaymentGateway (effectuer le débit),
    // ILogger (journalisation -> OpenTelemetry), IConfiguration (chaîne de connexion).
    // NOTE : le worker NE LIT PLUS Payment:AlwaysFail — c'est désormais la passerelle
    // (SimulatedPaymentGateway) qui gère ce détail de simulation.
    //   - ConsumerState : état partagé avec le health check ; on y positionne
    //                     IsConsuming=true une fois l'abonnement RabbitMQ effectif,
    //                     ce qui rend le worker « prêt » (readiness K8s).
    public PaymentWorker(
        IEventBus eventBus,
        IPaymentGateway gateway,
        ILogger<PaymentWorker> logger,
        ConsumerState consumerState,
        IConfiguration configuration)
    {
        _eventBus = eventBus;
        _gateway = gateway;
        _logger = logger;
        _consumerState = consumerState;
        // Chaîne de connexion injectée par Aspire via WithReference(rabbitmq) — pas d'URL en dur.
        _connectionString = configuration.GetConnectionString("rabbitmq")!;
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

        // BasicConsumeAsync renvoie le tag du consommateur : on le mémorise pour pouvoir
        // ANNULER la consommation à l'arrêt (voir StopAsync).
        _consumerTag = await _channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false, // ack manuel : on acquitte seulement après publication du résultat
            consumer: consumer,
            cancellationToken: stoppingToken);

        // À ce stade l'abonnement est effectif : on signale au health check que le worker
        // CONSOMME -> la readiness K8s passe au vert. Tant que ce drapeau est false, /health
        // reste « unhealthy » (le pod ne reçoit pas encore de trafic, mais /alive reste OK).
        _consumerState.IsConsuming = true;

        _logger.LogInformation(
            "PaymentProcessor démarré : écoute '{RoutingKey}', passerelle {Gateway}",
            StockConfirmedRoutingKey, _gateway.GetType().Name);

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
            "Stock confirmé pour la commande {OrderId} (event {EventId}) : traitement du paiement",
            evt.OrderId, evt.Id);

        try
        {
            // On traduit l'événement reçu en PaymentRequest, puis on délègue le débit
            // à la passerelle. Le worker IGNORE s'il s'agit d'une simulation ou d'un
            // vrai prestataire : il ne voit que l'abstraction IPaymentGateway.
            // ⚠️ PCI-DSS : on transmet ici un numéro de carte en clair UNIQUEMENT pour
            // la simulation ; en prod ce serait un jeton (jamais le PAN brut, ni en log).
            var request = new PaymentRequest(
                OrderId: evt.OrderId,
                BuyerId: evt.BuyerId,
                Amount: evt.Amount,
                CardNumber: evt.CardNumber,
                CardHolderName: evt.CardHolderName,
                CardExpiration: evt.CardExpiration);

            // ea.CancellationToken : jeton fourni par le consumer RabbitMQ pour cette
            // livraison (annulé à l'arrêt du worker), propagé à la passerelle.
            var result = await _gateway.ChargeAsync(request, ea.CancellationToken);

            // Dans les deux branches, on PUBLIE d'abord le résultat, PUIS on acquitte
            // (ack après publication réussie -> garantie at-least-once, voir plus bas).
            if (result.Succeeded)
            {
                var succeeded = new OrderPaymentSucceededIntegrationEvent
                {
                    OrderId = evt.OrderId,
                    BuyerId = evt.BuyerId,
                    // TransactionId garanti non-null par PaymentResult.Success.
                    TransactionId = result.TransactionId!
                };

                await _eventBus.PublishAsync(succeeded, PaymentSucceededRoutingKey);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);

                _logger.LogInformation(
                    "Paiement RÉUSSI pour la commande {OrderId} (transaction {TransactionId}) : '{RoutingKey}' publié",
                    evt.OrderId, succeeded.TransactionId, PaymentSucceededRoutingKey);
            }
            else
            {
                var failed = new OrderPaymentFailedIntegrationEvent
                {
                    OrderId = evt.OrderId,
                    BuyerId = evt.BuyerId,
                    Reason = result.FailureReason
                };

                await _eventBus.PublishAsync(failed, PaymentFailedRoutingKey);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);

                _logger.LogInformation(
                    "Paiement ÉCHOUÉ pour la commande {OrderId} ({Reason}) : '{RoutingKey}' publié",
                    evt.OrderId, failed.Reason, PaymentFailedRoutingKey);
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

    // Arrêt GRACIEUX : appelé par l'hôte sur SIGTERM (K8s) / Ctrl+C. L'ordre est important :
    //   1. ANNULER d'abord la consommation (BasicCancelAsync) : le broker cesse de nous
    //      livrer de nouveaux messages -> aucune livraison ne démarre alors qu'on ferme le
    //      canal (sinon on risque des exceptions ou des messages « happés » puis perdus) ;
    //   2. SEULEMENT ensuite, disposer le canal puis la connexion.
    // On marque aussi IsConsuming=false pour que la readiness retombe immédiatement (le pod
    // est retiré du service pendant le drain). Les messages non acquittés restent en queue et
    // seront redélivrés au prochain démarrage (le paiement sera donc rejoué -> idempotence).
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _consumerState.IsConsuming = false;

        // Annulation de la consommation, isolée dans un try/catch : un arrêt ne doit JAMAIS
        // échouer à cause d'un canal déjà fermé ou d'un broker injoignable -> on logue en warning.
        try
        {
            if (_channel is not null && _consumerTag is not null)
            {
                await _channel.BasicCancelAsync(_consumerTag, cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de l'annulation du consommateur RabbitMQ lors de l'arrêt.");
        }

        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
