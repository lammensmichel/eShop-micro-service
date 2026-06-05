# ============================================================================
# Dockerfile — Ordering.API  (image conteneur déployable en Kubernetes)
# ----------------------------------------------------------------------------
# CONTEXTE DE BUILD = RACINE DU DÉPÔT.
# Le projet référence d'autres projets (eShop.ServiceDefaults, eShop.IntegrationEvents).
# Construire depuis la racine :
#
#   docker build -f src/Ordering.API/Dockerfile -t ordering-api .
#
# Image MULTI-STAGE : SDK pour compiler, aspnet pour exécuter (image finale légère).
# ============================================================================

# --- STAGE 1 : BUILD -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1) Copie des .csproj (projet + dépendances projet) PUIS restore : couche en cache
#    tant que les .csproj ne changent pas (pattern de cache de couches).
COPY ["src/Ordering.API/Ordering.API.csproj", "src/Ordering.API/"]
COPY ["src/eShop.ServiceDefaults/eShop.ServiceDefaults.csproj", "src/eShop.ServiceDefaults/"]
COPY ["src/eShop.IntegrationEvents/eShop.IntegrationEvents.csproj", "src/eShop.IntegrationEvents/"]
RUN dotnet restore "src/Ordering.API/Ordering.API.csproj"

# 2) Copie du reste des sources puis publication en Release.
COPY . .
RUN dotnet publish "src/Ordering.API/Ordering.API.csproj" -c Release -o /app/publish --no-restore

# --- STAGE 2 : RUNTIME -----------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# SÉCURITÉ : exécution en utilisateur NON-ROOT « app » (fourni par l'image aspnet).
USER app

# Port HTTP interne standard K8s (8080, non privilégié).
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

# DLL d'entrée = nom du projet.
ENTRYPOINT ["dotnet", "Ordering.API.dll"]
