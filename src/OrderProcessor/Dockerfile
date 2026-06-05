# ============================================================================
# Dockerfile — OrderProcessor  (worker de la saga, déployable en Kubernetes)
# ----------------------------------------------------------------------------
# CONTEXTE DE BUILD = RACINE DU DÉPÔT.
# Le projet référence d'autres projets (eShop.ServiceDefaults, eShop.IntegrationEvents).
# Construire depuis la racine :
#
#   docker build -f src/OrderProcessor/Dockerfile -t order-processor .
#
# CHOIX DE L'IMAGE RUNTIME : aspnet:10.0 (et NON runtime:10.0).
#   Ce worker est en cours d'évolution vers un WebApplication minimal afin
#   d'exposer un endpoint /health (sondes liveness/readiness K8s). aspnet:10.0
#   embarque le runtime ASP.NET Core nécessaire pour écouter en HTTP sur /health.
#   On expose donc le port 8080 et on fixe ASPNETCORE_URLS comme pour les API.
#
# Image MULTI-STAGE : SDK pour compiler, aspnet pour exécuter (image finale légère).
# ============================================================================

# --- STAGE 1 : BUILD -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1) Copie des .csproj (projet + dépendances projet) PUIS restore : couche en cache
#    tant que les .csproj ne changent pas (pattern de cache de couches).
COPY ["src/OrderProcessor/OrderProcessor.csproj", "src/OrderProcessor/"]
COPY ["src/eShop.ServiceDefaults/eShop.ServiceDefaults.csproj", "src/eShop.ServiceDefaults/"]
COPY ["src/eShop.IntegrationEvents/eShop.IntegrationEvents.csproj", "src/eShop.IntegrationEvents/"]
RUN dotnet restore "src/OrderProcessor/OrderProcessor.csproj"

# 2) Copie du reste des sources puis publication en Release.
COPY . .
RUN dotnet publish "src/OrderProcessor/OrderProcessor.csproj" -c Release -o /app/publish --no-restore

# --- STAGE 2 : RUNTIME -----------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# SÉCURITÉ : exécution en utilisateur NON-ROOT « app » (fourni par l'image aspnet).
USER app

# Port HTTP interne (endpoint /health pour les sondes K8s).
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

# DLL d'entrée = nom du projet.
ENTRYPOINT ["dotnet", "OrderProcessor.dll"]
