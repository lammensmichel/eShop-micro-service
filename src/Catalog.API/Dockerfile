# ============================================================================
# Dockerfile — Catalog.API  (image conteneur déployable en Kubernetes)
# ----------------------------------------------------------------------------
# CONTEXTE DE BUILD = RACINE DU DÉPÔT (et non ce dossier).
# Le projet référence d'autres projets (eShop.ServiceDefaults), il faut donc que
# le contexte voie tout le dossier src/. Construire ainsi depuis la racine :
#
#   docker build -f src/Catalog.API/Dockerfile -t catalog-api .
#
# Image MULTI-STAGE : un gros SDK pour compiler, une petite image aspnet pour
# l'exécution (image finale légère, sans la chaîne de build).
# ============================================================================

# --- STAGE 1 : BUILD -------------------------------------------------------
# Le SDK contient tout le nécessaire pour restaurer/compiler/publier.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1) On copie D'ABORD uniquement les .csproj (projet + ses dépendances projet),
#    puis on restaure. Cette étape ne dépend QUE des .csproj : tant qu'ils ne
#    changent pas, Docker réutilise le cache de couches même si le code change.
COPY ["src/Catalog.API/Catalog.API.csproj", "src/Catalog.API/"]
COPY ["src/eShop.ServiceDefaults/eShop.ServiceDefaults.csproj", "src/eShop.ServiceDefaults/"]
RUN dotnet restore "src/Catalog.API/Catalog.API.csproj"

# 2) On copie ENSUITE tout le reste des sources et on publie en Release.
#    (Le bin/obj est exclu par le .dockerignore racine.)
COPY . .
RUN dotnet publish "src/Catalog.API/Catalog.API.csproj" -c Release -o /app/publish --no-restore

# --- STAGE 2 : RUNTIME -----------------------------------------------------
# aspnet:10.0 = runtime ASP.NET Core seul (image finale légère).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# SÉCURITÉ : on s'exécute en utilisateur NON-ROOT. L'image aspnet fournit déjà
# un utilisateur « app » sans privilèges : réduit la surface d'attaque (un process
# compromis n'a pas les droits root dans le conteneur). Bonne pratique K8s.
USER app

# Port HTTP interne standard exposé par le Service K8s (8080, non privilégié :
# accessible sans root, contrairement au port 80).
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# On récupère uniquement le résultat du publish depuis le stage de build.
COPY --from=build /app/publish .

# Point d'entrée : le nom de la DLL = nom du projet (pas d'AssemblyName custom).
ENTRYPOINT ["dotnet", "Catalog.API.dll"]
