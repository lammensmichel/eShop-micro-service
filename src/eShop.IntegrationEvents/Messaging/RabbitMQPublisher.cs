using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace eShop.IntegrationEvents.Messaging;

// =============================================================================
// FICHIER : RabbitMQPublisher.cs
// RÔLE    : implémentation concrète d'IEventBus au-dessus de RabbitMQ. C'est le
//           code qui transforme un objet C# en message réseau fiable.
// CONCEPT : PUBLICATION FIABLE SUR UN BROKER DE MESSAGES.
//
// VOCABULAIRE RABBITMQ (à connaître pour lire ce fichier) :
//   - Broker      : le serveur RabbitMQ lui-même (un conteneur ici, lancé par l'AppHost).
//   - Connection  : socket TCP vers le broker. Coûteuse à ouvrir -> on la PARTAGE.
//   - Channel     : "session" légère multiplexée sur une connexion. C'est par un
//                   channel qu'on publie. Un channel n'est PAS thread-safe.
//   - Exchange    : le "routeur" du broker. On lui envoie le message ; lui décide
//                   dans quelle(s) queue(s) le déposer. Ici type "direct".
//   - Queue       : la file où les messages attendent d'être lus par un consommateur.
//   - Routing key : étiquette du message qu'un exchange "direct" compare à la clé
//                   de liaison (binding) d'une queue pour router (cf. IEventBus).
//
// SCHÉMA DU TRAJET D'UN MESSAGE :
//
//   Basket.API ──PublishAsync(evt,"basket-checkout")──┐
//                                                      ▼
//        [ Connection partagée ] → [ Channel éphémère (1 par publication) ]
//                                                      │ BasicPublish
//                                                      ▼
//                         exchange "eshop_event_bus" (direct, durable)
//                                   │  route selon la routing key
//                                   ▼
//                         queue liée à "basket-checkout"
//                                   ▼
//        Ordering.API (RabbitMQConsumer) lit, ack, crée la commande
//
// DEUX GARANTIES DE FIABILITÉ activées ici (détaillées dans PublishAsync) :
//   1. publisher confirms : l'await ne rend la main qu'après l'ACK du broker.
//   2. mandatory:true      : si AUCUNE queue n'est liée à la routing key, le
//                            message non routable provoque une erreur au lieu
//                            d'être perdu en silence.
//
// PLACE DANS LE FLUX : appelé par Basket.API/Apis/BasketApi.cs (checkout).
//   Le pendant côté réception est Ordering.API/Infrastructure/Messaging/
//   RabbitMQConsumer.cs : l'exchange ("eshop_event_bus") et la routing key
//   ("basket-checkout") DOIVENT y être identiques, sinon rien n'est routé.
//
// À LIRE :
//   - AVANT : IEventBus.cs, IntegrationEvent.cs.
//   - APRÈS : EventBusExtensions.cs (enregistrement DI), puis côté Basket.API
//             Program.cs et BasketApi.cs.
// =============================================================================

/// <summary>
/// Publisher RabbitMQ robuste : la connexion est partagée et réutilisée (ouverte
/// paresseusement et ré-ouverte si elle est tombée), mais un <see cref="IChannel"/>
/// est créé puis disposé à chaque publication. Les channels RabbitMQ ne sont PAS
/// thread-safe : en ouvrir un par appel évite tout partage concurrent.
/// </summary>
public class RabbitMQPublisher : IEventBus, IAsyncDisposable
{
    private readonly string _connectionString;

    // Nom de l'exchange partagé par TOUT le système. Cette constante doit être
    // identique côté consommateur (Ordering.API/RabbitMQConsumer) : producteur et
    // consommateur déclarent le même exchange, sinon les messages ne se rejoignent pas.
    private const string ExchangeName = "eshop_event_bus";

    // Verrou asynchrone (un sémaphore à 1 jeton) protégeant la (ré)ouverture de la
    // connexion partagée. Plusieurs requêtes HTTP peuvent appeler PublishAsync en
    // parallèle ; sans ce verrou, deux threads pourraient ouvrir deux connexions
    // concurrentes au moment où la connexion est nulle/cassée. C'est ce qu'on
    // appelle une "connexion partagée thread-safe".
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;
    private bool _disposed;

    public RabbitMQPublisher(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task PublishAsync<T>(T message, string routingKey) where T : IntegrationEvent
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var connection = await GetOrCreateConnectionAsync();

        // SÉRIALISATION PAR TYPE RUNTIME (subtilité importante).
        // On passe message.GetType() (le type RÉEL de l'objet à l'exécution) plutôt
        // que de laisser le compilateur sérialiser selon T (le type statique).
        // Pourquoi ? System.Text.Json ne sérialise QUE les propriétés du type qu'on
        // lui indique. Si T vaut IntegrationEvent (cas du "republish" depuis une table
        // outbox, où le contenu a été désérialisé vers le type de base), alors
        // Serialize<T> n'émettrait que Id/CreationDate et JETTERAIT les champs propres
        // à l'événement concret (OrderId, BuyerId, Items...). Le consommateur recevrait
        // un message amputé. En forçant le type runtime, on émet TOUT le JSON utile.
        //   T = IntegrationEvent (statique)  →  Serialize<T> : {Id, CreationDate}        ✗
        //   message.GetType() = BasketCheckoutEvent (runtime) → {Id, CreationDate, BuyerId, ...} ✓
        var json = JsonSerializer.Serialize(message, message.GetType());
        var body = Encoding.UTF8.GetBytes(json);

        var props = new BasicProperties
        {
            // DeliveryMode.Persistent : RabbitMQ écrit le message sur disque. Couplé à
            // une queue durable côté consommateur, le message survit à un redémarrage
            // du broker (il n'est pas qu'en RAM). C'est le complément naturel de la
            // fiabilité visée ici.
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json"
        };

        // UN CHANNEL PAR PUBLICATION : créé puis disposé immédiatement (cf. "à using").
        // Rappel : un channel n'est PAS thread-safe. Plutôt que de synchroniser un
        // channel partagé (lent et fragile), on en ouvre un dédié à cet appel ; il
        // disparaît à la fin du bloc. La connexion sous-jacente, elle, reste partagée.
        //
        // PUBLISHER CONFIRMS (avec suivi) : c'est un accusé de réception du broker.
        // Sans ça, BasicPublish renvoie la main dès que le message part sur le socket,
        // sans savoir si le broker l'a réellement accepté. Avec confirms activés,
        // l'await de BasicPublishAsync ne se termine QU'APRÈS l'ACK du broker (ou lève
        // si NACK). Conséquence concrète exploitée par BasketApi : on publie AVANT de
        // vider le panier, certains que l'événement est pris en charge.
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);

        await using var channel = await connection.CreateChannelAsync(channelOptions);

        // Déclaration idempotente de l'exchange : si "eshop_event_bus" existe déjà
        // (avec les mêmes paramètres), RabbitMQ ne fait rien ; sinon il le crée.
        // Producteur ET consommateur le déclarent, ainsi l'ordre de démarrage des
        // services n'a pas d'importance. type=Direct -> routage par égalité stricte
        // de routing key. durable=true -> l'exchange survit à un redémarrage du broker.
        await channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Direct,
            durable: true);

        // mandatory:true : exige que le message soit ROUTABLE. Si aucune queue n'est
        // liée à cette routing key (ex. Ordering.API jamais démarré, donc sa queue
        // n'existe pas), le broker renvoie le message ("basic.return") et l'opération
        // échoue par une exception, au lieu de l'avaler en silence. On préfère un échec
        // bruyant à une commande perdue. Combiné aux publisher confirms ci-dessus, cet
        // await ne se termine donc que si le message est (a) accepté par le broker ET
        // (b) effectivement déposé dans au moins une queue.
        await channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: routingKey,
            mandatory: true,
            basicProperties: props,
            body: body);
    }

    /// <summary>
    /// Vérifie que la connexion RabbitMQ peut être ouverte et qu'un channel peut y
    /// être créé. Utilisé par le health check.
    /// </summary>
    public async Task CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetOrCreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Retourne la connexion partagée, en l'ouvrant (ou ré-ouvrant) de façon
    /// asynchrone et thread-safe si elle n'existe pas ou si elle est fermée.
    /// </summary>
    private async Task<IConnection> GetOrCreateConnectionAsync()
    {
        var existing = _connection;
        if (existing is { IsOpen: true })
        {
            return existing;
        }

        await _connectionLock.WaitAsync();
        try
        {
            // Double vérification après acquisition du verrou.
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            // Connexion fermée/cassée : on la dispose avant d'en recréer une.
            if (_connection is not null)
            {
                try
                {
                    await _connection.DisposeAsync();
                }
                catch
                {
                    // On ignore les erreurs de fermeture d'une connexion déjà morte.
                }

                _connection = null;
            }

            var factory = new ConnectionFactory { Uri = new Uri(_connectionString) };
            _connection = await factory.CreateConnectionAsync();
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
