# ============================================================================
# Dockerfile — Identity.API  (image conteneur déployable en Kubernetes)
# ----------------------------------------------------------------------------
# CONTEXTE DE BUILD = RACINE DU DÉPÔT.
# Le projet référence un autre projet (eShop.ServiceDefaults).
# Construire depuis la racine :
#
#   docker build -f src/Identity.API/Dockerfile -t identity-api .
#
# Image MULTI-STAGE : SDK pour compiler, aspnet pour exécuter (image finale légère).
# ============================================================================

# --- STAGE 1 : BUILD -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1) Copie des .csproj (projet + dépendances projet) PUIS restore : couche en cache
#    tant que les .csproj ne changent pas (pattern de cache de couches).
COPY ["src/Identity.API/Identity.API.csproj", "src/Identity.API/"]
COPY ["src/eShop.ServiceDefaults/eShop.ServiceDefaults.csproj", "src/eShop.ServiceDefaults/"]
RUN dotnet restore "src/Identity.API/Identity.API.csproj"

# 2) Copie du reste des sources puis publication en Release.
COPY . .
RUN dotnet publish "src/Identity.API/Identity.API.csproj" -c Release -o /app/publish --no-restore

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
ENTRYPOINT ["dotnet", "Identity.API.dll"]
