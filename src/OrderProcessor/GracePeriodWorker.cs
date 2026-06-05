using System.Text;
using System.Text.Json;
using eShop.IntegrationEvents.Events;
using eShop.IntegrationEvents.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderProcessor;

// ============================================================================
// GracePeriodWorker — l'étape « période de grâce » de la saga de commande.
// ----------------------------------------------------------------------------
// RÔLE : entre le moment où la commande est SOUMISE et celui où Ordering la
// confirme, on laisse à l'acheteur une fenêtre d'annulation : la « période de
// grâce ». Ce worker matérialise cette fenêtre. Concrètement il :
//   1. CONSOMME  OrderStatusChangedToSubmittedIntegrationEvent
//                (routing key "ordering-order-submitted") ;
//   2. ATTEND    une période de grâce configurable (défaut 10 s) ;
//   3. PUBLIE    GracePeriodConfirmedIntegrationEvent
//                (routing key "ordering-grace-period-confirmed").
//
// CONCEPT ILLUSTRÉ — le « worker » (BackgroundService) + la « chorégraphie saga ».
//   - Un BackgroundService est un service de fond hébergé : pas d'API HTTP, juste
//     une boucle qui tourne tant que l'application vit. .NET appelle ExecuteAsync
//     au démarrage et fournit un CancellationToken déclenché à l'arrêt.
//   - Une saga « chorégraphiée » coordonne une transaction longue répartie sur
//     plusieurs services SANS chef d'orchestre central : chaque service réagit à
//     un événement reçu et en émet un nouveau. Le flux complet n'existe nulle part
//     en un seul endroit ; il « émerge » de la somme des réactions. Ce worker est
//     un maillon de cette chaîne (voir le schéma complet dans AppHost.cs).
//
// PLACE DANS L'ENSEMBLE :
//   Ordering -- ordering-order-submitted --> [CE WORKER] -- ordering-grace-period-confirmed --> Ordering
//
// CONCEPTS RabbitMQ utilisés (pattern calqué sur le RabbitMQConsumer d'Ordering) :
//   - exchange "direct" durable partagé "eshop_event_bus" : un aiguilleur qui route
//     chaque message vers les queues liées à SA routing key (correspondance exacte) ;
//   - queue durable propre au worker : survit aux redémarrages du broker ;
//   - liaison (binding) queue<->exchange sur la routing key écoutée ;
//   - autoAck:false : accusé de réception MANUEL (on contrôle quand le message est
//     « consommé pour de bon ») ;
//   - prefetch=1 : au plus un message non acquitté à la fois ;
//   - ack après publication réussie ; nack(requeue:false) si message illisible
//     (échec déterministe : le rejouer ne servirait à rien -> rejet définitif).
//
// IDEMPOTENCE / REJOUABILITÉ : un message peut être redélivré (redémarrage, requeue).
// Le traitement doit donc pouvoir être rejoué sans dommage. Ici, republier
// GracePeriodConfirmed pour la même commande est inoffensif côté Ordering, qui
// ignore une confirmation déjà appliquée (transition d'état idempotente).
public class GracePeriodWorker : BackgroundService
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<GracePeriodWorker> _logger;
    private readonly ConsumerState _consumerState;
    private readonly string _connectionString;
    private readonly TimeSpan _gracePeriod;
    private IConnection? _connection;
    private IChannel? _channel;
    private CancellationToken _stoppingToken;

    // Tag du consommateur renvoyé par BasicConsumeAsync : nécessaire pour ANNULER
    // proprement la consommation (BasicCancelAsync) lors de l'arrêt gracieux.
    private string? _consumerTag;

    private const string ExchangeName = "eshop_event_bus";

    // Queue durable propre à ce worker.
    private const string QueueName = "orderprocessor-events";

    // Routing key consommée : commande venant d'être soumise.
    private const string SubmittedRoutingKey = "ordering-order-submitted";

    // Routing key publiée : grace period écoulée.
    private const string GracePeriodConfirmedRoutingKey = "ordering-grace-period-confirmed";

    // Les dépendances sont injectées par le conteneur DI (configuré dans Program.cs) :
    //   - IEventBus  : le bus partagé pour PUBLIER l'événement suivant (abstraction
    //                  au-dessus de RabbitMQ -> on ne dépend pas de l'implémentation) ;
    //   - ILogger    : journalisation, remontée vers OpenTelemetry via ServiceDefaults ;
    //   - IConfiguration : accès aux réglages (chaîne de connexion, durée de grâce).
    //   - ConsumerState : état partagé avec le health check ; on y positionne
    //                     IsConsuming=true une fois l'abonnement RabbitMQ effectif,
    //                     ce qui rend le worker « prêt » (readiness K8s).
    public GracePeriodWorker(
        IEventBus eventBus,
        ILogger<GracePeriodWorker> logger,
        ConsumerState consumerState,
        IConfiguration configuration)
    {
        _eventBus = eventBus;
        _logger = logger;
        _consumerState = consumerState;
        // La chaîne de connexion "rabbitmq" n'est PAS codée en dur : Aspire l'injecte
        // dans la configuration grâce au WithReference(rabbitmq) déclaré dans l'AppHost.
        _connectionString = configuration.GetConnectionString("rabbitmq")!;

        // Période de grâce configurable via "GracePeriod:Seconds" (défaut 10 s).
        // Volontairement courte ici pour observer la saga rapidement en démo.
        var seconds = configuration.GetValue("GracePeriod:Seconds", 10);
        _gracePeriod = TimeSpan.FromSeconds(seconds);
    }

    // ExecuteAsync = la boucle de fond du worker. Appelée une fois au démarrage par
    // l'hôte .NET. On y établit la connexion RabbitMQ, on déclare la topologie
    // (exchange + queue + binding), on s'abonne, puis on « dort » jusqu'à l'arrêt.
    // Le stoppingToken est déclenché quand l'application s'arrête (Ctrl+C, SIGTERM…).
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Mémorisé en champ pour que le handler d'événement (OnMessageReceivedAsync,
        // appelé plus tard par la lib RabbitMQ) puisse propager l'annulation à Task.Delay.
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

        // Le consommateur est piloté par événement : à chaque message reçu, la lib
        // RabbitMQ déclenche ReceivedAsync -> notre handler OnMessageReceivedAsync.
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        // BasicConsumeAsync renvoie le tag du consommateur : on le mémorise pour pouvoir
        // ANNULER la consommation à l'arrêt (voir StopAsync).
        _consumerTag = await _channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false, // accusé manuel : c'est NOUS qui décidons quand acquitter
            consumer: consumer,
            cancellationToken: stoppingToken);

        // À ce stade l'abonnement est effectif : on signale au health check que le worker
        // CONSOMME -> la readiness K8s passe au vert. Tant que ce drapeau est false, /health
        // reste « unhealthy » (le pod ne reçoit pas encore de trafic, mais /alive reste OK).
        _consumerState.IsConsuming = true;

        _logger.LogInformation(
            "OrderProcessor démarré : écoute '{RoutingKey}', période de grâce {Grace}s",
            SubmittedRoutingKey, _gracePeriod.TotalSeconds);

        // La consommation est désormais pilotée par les callbacks ci-dessus. Il ne
        // reste plus qu'à empêcher ExecuteAsync de se terminer (ce qui arrêterait le
        // worker) : on attend « indéfiniment » jusqu'à ce que le stoppingToken annule
        // ce Task.Delay (à l'arrêt de l'application).
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    // Handler appelé par RabbitMQ pour CHAQUE message livré sur notre queue.
    // ea (BasicDeliverEventArgs) porte le corps brut + le DeliveryTag, l'identifiant
    // de livraison qu'on utilise pour acquitter (ack) ou rejeter (nack) le message.
    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var channel = _channel!;
        var stoppingToken = _stoppingToken;
        // Le corps du message est un tableau d'octets (le JSON de l'événement sérialisé).
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

            // Ordre IMPORTANT : on acquitte SEULEMENT après une publication réussie.
            // Ainsi, si la publication échoue, le catch ci-dessous requeue le message
            // et on réessaiera plus tard. C'est une garantie « at-least-once » : on
            // préfère traiter potentiellement deux fois (d'où le besoin d'idempotence)
            // plutôt que de perdre un message en l'acquittant trop tôt.
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

    // Arrêt GRACIEUX : appelé par l'hôte sur SIGTERM (K8s) / Ctrl+C. L'ordre est important :
    //   1. ANNULER d'abord la consommation (BasicCancelAsync) : le broker cesse de nous
    //      livrer de nouveaux messages -> aucune livraison ne démarre alors qu'on ferme le
    //      canal (sinon on risque des exceptions ou des messages « happés » puis perdus) ;
    //   2. SEULEMENT ensuite, disposer le canal puis la connexion.
    // On marque aussi IsConsuming=false pour que la readiness retombe immédiatement (le pod
    // est retiré du service pendant le drain). Tout message non acquitté reste en queue et
    // sera redélivré au prochain démarrage (garantie at-least-once).
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
