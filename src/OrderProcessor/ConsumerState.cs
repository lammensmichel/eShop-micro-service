namespace OrderProcessor;

// ============================================================================
// ConsumerState — petit ÉTAT PARTAGÉ entre le worker et son health check.
// ----------------------------------------------------------------------------
// POURQUOI : la readiness K8s doit savoir si le worker a réellement DÉMARRÉ sa
// consommation RabbitMQ (BasicConsumeAsync effectué), pas seulement si le process
// est en vie. Le BackgroundService (GracePeriodWorker) positionne IsConsuming=true
// une fois abonné ; le WorkerHealthCheck lit ce drapeau. On passe par un singleton
// partagé injecté des deux côtés (plutôt qu'un état statique) pour rester testable
// et explicite dans le conteneur DI.
//
// THREAD-SAFETY : un bool est lu/écrit de façon atomique en .NET ; on le marque
// volatile pour garantir que l'écriture faite par le thread du worker soit vue
// immédiatement par le thread qui sert la sonde HTTP.
public sealed class ConsumerState
{
    private volatile bool _isConsuming;

    // true une fois que le worker s'est abonné à la queue (consommation active).
    public bool IsConsuming
    {
        get => _isConsuming;
        set => _isConsuming = value;
    }
}
