using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace eShop.IntegrationEvents.Messaging;

/// <summary>
/// Publisher RabbitMQ robuste : la connexion est partagée et réutilisée (ouverte
/// paresseusement et ré-ouverte si elle est tombée), mais un <see cref="IChannel"/>
/// est créé puis disposé à chaque publication. Les channels RabbitMQ ne sont PAS
/// thread-safe : en ouvrir un par appel évite tout partage concurrent.
/// </summary>
public class RabbitMQPublisher : IEventBus, IAsyncDisposable
{
    private readonly string _connectionString;
    private const string ExchangeName = "eshop_event_bus";

    // Verrou asynchrone protégeant la (ré)ouverture de la connexion partagée.
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

        // On sérialise selon le type RUNTIME (message.GetType()) et non selon T : si T est
        // le type de base IntegrationEvent (cas du republish outbox, qui désérialise le
        // Content vers IntegrationEvent), JsonSerializer.Serialize<T> n'émettrait que les
        // propriétés de la base (Id/CreationDate) et perdrait les champs dérivés
        // (OrderId/BuyerId, etc.), rendant le message illisible côté consommateur.
        var json = JsonSerializer.Serialize(message, message.GetType());
        var body = Encoding.UTF8.GetBytes(json);

        var props = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json"
        };

        // Un channel par publication : créé puis disposé immédiatement (les channels
        // ne sont pas thread-safe, on n'en partage donc jamais entre threads).
        // On active les publisher confirms (avec suivi) : BasicPublishAsync ne se
        // termine alors qu'une fois le broker ayant accusé réception du message
        // (ack), garantissant qu'il a bien été pris en charge.
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);

        await using var channel = await connection.CreateChannelAsync(channelOptions);

        await channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Direct,
            durable: true);

        // mandatory:true : si aucune queue n'est liée à la routing key, le message
        // n'est pas routable et le broker le renvoie -> on lève une exception plutôt
        // que de perdre silencieusement la commande. Avec les publisher confirms,
        // l'await ne se termine qu'après l'accusé de réception du broker.
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
