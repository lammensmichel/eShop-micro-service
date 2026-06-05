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
// RÔLE : factoriser, sous forme de méthodes d'extension, toute la plomberie
// transverse (cross-cutting concerns) qu'un service distribué doit configurer.
// Plutôt que de recopier la même configuration dans chaque Program.cs (Catalog,
// Basket, Ordering, Identity, WebApp, et les deux workers OrderProcessor/
// PaymentProcessor), chaque service appelle simplement AddServiceDefaults() — et,
// côté API web seulement, MapDefaultEndpoints() — au démarrage.
//
// CE QUE LE SOCLE APPORTE (et le jargon associé) :
//   - OpenTelemetry : standard d'observabilité unifié, trois SIGNAUX —
//       * logs    : messages journalisés ;
//       * métriques : compteurs/jauges (req/s, durées, GC...) ;
//       * traces  : « spans » reliés qui suivent UNE requête à travers PLUSIEURS
//                   services (traçage distribué). Tout est exporté en OTLP
//                   (OpenTelemetry Protocol) vers le collecteur du dashboard Aspire.
//   - Health checks : sondes de santé, avec deux notions distinctes —
//       * liveness  (/alive)  : « le process est-il vivant ? » ;
//       * readiness (/health) : « peut-il accepter du trafic ? » (toutes deps OK).
//   - Résilience HTTP via Polly : retries, circuit breaker, timeouts appliqués
//     automatiquement à tous les HttpClient (une dépendance distante peut flancher).
//   - Service discovery : appeler un service par son NOM logique, pas une URL en dur.
//   - AddDefaultAuthentication : validation des jetons JWT émis par Identity (appelée
//     par les API protégées ; PAS par les workers, qui n'exposent aucune API HTTP).
//
// L'intérêt pédagogique : voir comment Aspire mutualise ces préoccupations transverses.
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
                // RequireHttpsMetadata : exige (ou non) que la récupération des métadonnées
                // OIDC de l'authority se fasse en HTTPS strict.
                //   - DEV LOCAL (Aspire) : l'authority est en http(s) localhost et le certificat
                //     de dev peut poser problème -> on garde le défaut false (comportement inchangé).
                //   - PROD K8s : l'Identity est servi derrière TLS ; on surcharge à true via la
                //     config/env (clé "Identity__RequireHttpsMetadata=true") pour empêcher toute
                //     récupération de métadonnées en clair (sécurité).
                options.RequireHttpsMetadata = builder.Configuration.GetValue<bool>("Identity:RequireHttpsMetadata", false);
                // Aucune ApiResource n'est définie dans Identity.API (uniquement des ApiScopes),
                // donc le jeton ne porte pas de claim "aud" -> on ne valide pas l'audience.
                options.TokenValidationParameters.ValidateAudience = false;
                // On désactive le mapping « hérité » des claims entrants : sinon le handler
                // renomme "role"/"name" vers leurs URI longs (…/claims/role, …/claims/name),
                // et comme on déclare RoleClaimType="role"/NameClaimType="name" (noms courts),
                // plus rien ne correspond -> IsInRole("Admin") échoue (403 sur les endpoints
                // [Authorize(Roles="Admin")]). MapInboundClaims=false conserve les noms bruts.
                options.MapInboundClaims = false;
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
        //
        // EN PROD K8s : cette même variable OTEL_EXPORTER_OTLP_ENDPOINT est injectée dans le pod
        // (via Deployment env/ConfigMap) et pointe vers le collecteur OpenTelemetry du cluster
        // (ex. "http://otel-collector.observability:4317"), qui relaie ensuite vers le backend
        // (Tempo/Jaeger, Prometheus, Loki...). Le code reste identique : seule la valeur de la
        // variable change selon l'environnement. Si elle est absente, on n'enregistre AUCUN
        // exporter -> le service démarre quand même normalement (pas de plantage).
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
        // ------------------------------------------------------------------
        // EN-TÊTES DE SÉCURITÉ (security headers)
        // ------------------------------------------------------------------
        // Middleware léger qui pose des en-têtes de durcissement sur TOUTES les
        // réponses HTTP. Ces en-têtes sont sans risque en dev comme en prod :
        //   - X-Content-Type-Options=nosniff : empêche le navigateur de « deviner »
        //     un type MIME différent de celui annoncé (anti MIME-sniffing) ;
        //   - X-Frame-Options=DENY : interdit l'inclusion dans un <iframe> (anti-clickjacking) ;
        //   - Referrer-Policy=no-referrer : ne divulgue pas l'URL d'origine aux tiers.
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            await next();
        });

        // HSTS (HTTP Strict Transport Security) : force le navigateur à n'utiliser que
        // HTTPS pour ce domaine. On ne l'active QUE hors Development :
        //   - DEV LOCAL : on tourne en http/localhost avec certificat de dev -> activer
        //     HSTS « épinglerait » HTTPS dans le navigateur et casserait l'expérience dev.
        //   - PROD K8s : le trafic arrive derrière un Ingress/terminaison TLS -> HSTS a du sens.
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        // ------------------------------------------------------------------
        // SONDES DE SANTÉ (health checks)
        // ------------------------------------------------------------------
        // CIBLE K8s : kubelet sonde liveness/readiness via HTTP. Ces endpoints DOIVENT
        // donc être exposés en permanence (et pas seulement en dev), sinon les probes
        // échouent et le pod est tué ou retiré du service -> impossible de déployer en
        // zéro-interruption. On retire donc l'ancien gate IsDevelopment().
        // (En prod, restreindre l'accès réseau à ces routes relève de l'Ingress/NetworkPolicy,
        //  pas de ce socle.) Voir https://aka.ms/aspire/healthchecks.

        // /health (readiness) : TOUS les checks doivent passer (dépendances incluses) ->
        // le service est prêt à recevoir du trafic. C'est ce que la readinessProbe K8s
        // (et WaitFor de l'AppHost en dev) interroge.
        app.MapHealthChecks(HealthEndpointPath);

        // /alive (liveness) : seuls les checks taggés "live" doivent passer (self uniquement,
        // pas les dépendances) -> le process est vivant même si une dépendance n'est pas
        // encore prête. C'est ce que la livenessProbe K8s interroge ; on ne veut pas qu'une
        // dépendance momentanément KO provoque le redémarrage du pod.
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}
