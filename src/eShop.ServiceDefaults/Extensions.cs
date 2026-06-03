using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// ============================================================================
// eShop.ServiceDefaults — le « socle commun » de TOUS les services Aspire.
// ----------------------------------------------------------------------------
// Plutôt que de recopier la même plomberie d'observabilité/résilience/santé dans
// chaque Program.cs (Catalog, Basket, Ordering, Identity, WebApp, et les deux
// workers OrderProcessor/PaymentProcessor), on la factorise ici sous forme de
// méthodes d'extension. Chaque service appelle simplement AddServiceDefaults()
// (et MapDefaultEndpoints() côté API web) au démarrage.
//
// L'intérêt pédagogique : voir comment Aspire mutualise les préoccupations
// transverses (cross-cutting concerns) d'un système distribué.
// Doc : https://aka.ms/aspire/service-defaults
// ============================================================================
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    // Point d'entrée unique appelé par chaque service. Active, d'un seul coup :
    //   - OpenTelemetry (logs/metrics/traces) ;
    //   - les health checks par défaut ;
    //   - la découverte de services (résolution des noms logiques -> URLs) ;
    //   - la résilience HTTP par défaut sur tous les HttpClient.
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        // Service discovery : permet d'appeler un autre service par son NOM logique
        // (ex. "http://catalog-api") au lieu d'une URL codée en dur. Aspire injecte
        // la correspondance nom -> URL réelle via la configuration (WithReference).
        builder.Services.AddServiceDiscovery();

        // Configuration appliquée à TOUS les HttpClient créés par le service.
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Résilience par défaut : retries, circuit breaker, timeouts... (pile
            // Polly intégrée). Indispensable entre microservices où une dépendance
            // peut être momentanément indisponible.
            http.AddStandardResilienceHandler();

            // Active la découverte de services sur le client : il sait résoudre les
            // noms logiques en URLs concrètes.
            http.AddServiceDiscovery();
        });

        // Décommenter pour restreindre les schémas autorisés par la découverte de
        // services (ex. n'autoriser que HTTPS).
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    // Configure la validation des jetons JWT (Bearer) émis par Identity.API.
    // L'URL d'Identity est fournie par l'AppHost via la variable d'environnement
    // "Identity__Url" (clé de config "Identity:Url"), ce qui garantit que l'authority
    // validée ici est la même que l'issuer vu par le front -> les jetons sont acceptés.
    // Si la clé n'est pas configurée, l'authentification n'est pas activée (le service
    // peut alors tourner seul, sans Identity).
    public static TBuilder AddDefaultAuthentication<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var identityUrl = builder.Configuration["Identity:Url"];
        if (string.IsNullOrEmpty(identityUrl))
        {
            return builder;
        }

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = identityUrl;
                // localhost en dev : autorise la récupération des métadonnées sans exiger HTTPS strict.
                options.RequireHttpsMetadata = false;
                // Aucune ApiResource n'est définie dans Identity.API (uniquement des ApiScopes),
                // donc le jeton ne porte pas de claim "aud" -> on ne valide pas l'audience.
                options.TokenValidationParameters.ValidateAudience = false;
                // Aligne les claims sur ceux émis par CustomProfileService.
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = "role";
            });

        builder.Services.AddAuthorization();

        return builder;
    }

    // Configure OpenTelemetry, le standard d'observabilité unifié de tous les services.
    // Les trois signaux (logs, métriques, traces) sont exportés (voir AddOpenTelemetryExporters)
    // vers le collecteur OTLP qu'Aspire branche automatiquement sur son tableau de bord :
    // c'est ainsi qu'on voit les logs corrélés et le traçage distribué de bout en bout.
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // LOGS : enrichit les logs envoyés à OpenTelemetry (message formaté + scopes).
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            // MÉTRIQUES : ASP.NET Core (requêtes), HttpClient (appels sortants),
            // et runtime .NET (GC, threads, mémoire...).
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            // TRACES (spans) : permettent de suivre une requête à travers plusieurs services.
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // On exclut les appels de health check du traçage : sinon le dashboard
                        // serait noyé sous les pings /health et /alive répétés.
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Décommenter pour tracer gRPC (nécessite le package OpenTelemetry.Instrumentation.GrpcNetClient).
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    // Choisit où exporter les signaux OpenTelemetry.
    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // Aspire injecte la variable OTEL_EXPORTER_OTLP_ENDPOINT (URL de son collecteur)
        // dans chaque service : sa simple présence suffit à activer l'export OTLP, qui
        // alimente le tableau de bord. En dehors d'Aspire, la variable est absente -> pas d'export.
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Décommenter pour exporter vers Azure Monitor (nécessite le package Azure.Monitor.OpenTelemetry.AspNetCore).
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    // Enregistre les health checks par défaut. On distingue deux notions :
    //   - « live » (liveness)  : le process répond-il ? (sinon -> redémarrer)
    //   - « ready » (readiness) : peut-il accepter du trafic ? (toutes les dépendances OK)
    // Ici on ajoute un check « self » trivial, taggé "live", qui répond toujours sain :
    // il prouve simplement que le host tourne. Les services peuvent en ajouter d'autres
    // (Postgres, Redis...) qui, eux, comptent pour la readiness.
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    // Expose les endpoints HTTP de santé. Appelé uniquement par les services web (les
    // workers OrderProcessor/PaymentProcessor n'ont pas de pipeline HTTP et ne l'appellent pas).
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Exposer ces endpoints en production a des implications de sécurité (ils révèlent
        // l'état interne) : on les limite donc à l'environnement de développement.
        // Voir https://aka.ms/aspire/healthchecks avant de les activer en production.
        if (app.Environment.IsDevelopment())
        {
            // /health (readiness) : TOUS les checks doivent passer -> le service est prêt à
            // recevoir du trafic. C'est ce que WaitFor de l'AppHost interroge.
            app.MapHealthChecks(HealthEndpointPath);

            // /alive (liveness) : seuls les checks taggés "live" doivent passer -> le service
            // est simplement vivant (même si une dépendance n'est pas encore prête).
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}
