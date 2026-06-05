# ============================================================================
# Dockerfile — WebApp.Server  (héberge le client Blazor WebAssembly « WebApp »)
# ----------------------------------------------------------------------------
# CONTEXTE DE BUILD = RACINE DU DÉPÔT.
# WebApp.Server référence :
#   - WebApp (Blazor WebAssembly) : le bundle WASM (dotnet.wasm, DLLs, wwwroot)
#     est produit et embarqué AUTOMATIQUEMENT dans le publish grâce à la
#     ProjectReference + au tooling Blazor (static web assets). Aucune étape
#     manuelle : `dotnet publish` du serveur produit le site complet servable.
#   - eShop.ServiceDefaults (socle transverse).
#
# Construire depuis la racine :
#
#   docker build -f src/WebApp.Server/Dockerfile -t webapp-server .
#
# Image MULTI-STAGE : SDK pour compiler, aspnet pour servir (image finale légère).
# ============================================================================

# --- STAGE 1 : BUILD -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1) Copie des .csproj (serveur + client WASM + dépendances projet) PUIS restore :
#    couche en cache tant que les .csproj ne changent pas (cache de couches).
COPY ["src/WebApp.Server/WebApp.Server.csproj", "src/WebApp.Server/"]
COPY ["src/WebApp/WebApp.csproj", "src/WebApp/"]
COPY ["src/eShop.ServiceDefaults/eShop.ServiceDefaults.csproj", "src/eShop.ServiceDefaults/"]
RUN dotnet restore "src/WebApp.Server/WebApp.Server.csproj"

# 2) Copie du reste des sources puis publication en Release.
#    Le publish du serveur entraîne la compilation du client WASM et l'inclusion
#    de son bundle (wwwroot/_framework/...) dans /app/publish/wwwroot.
COPY . .
RUN dotnet publish "src/WebApp.Server/WebApp.Server.csproj" -c Release -o /app/publish --no-restore

# --- STAGE 2 : RUNTIME -----------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# SÉCURITÉ : exécution en utilisateur NON-ROOT « app » (fourni par l'image aspnet).
USER app

# Port HTTP interne standard K8s (8080, non privilégié).
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

# DLL d'entrée = nom du projet serveur.
ENTRYPOINT ["dotnet", "WebApp.Server.dll"]
