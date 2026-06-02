# Run

dotnet build
dotnet run --project src/eShop.AppHost/eShop.AppHost.csproj

# EF

## installe ef

dotnet add src/Catalog.API/Catalog.API.csproj package Microsoft.EntityFrameworkCore
dotnet add src/Catalog.API/Catalog.API.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Catalog.API/Catalog.API.csproj package Microsoft.EntityFrameworkCore.Design

### pour aspire

dotnet add src/eShop.AppHost/eShop.AppHost.csproj package Aspire.Hosting.PostgreSql

## install outil migration :

dotnet tool install --global dotnet-ef

## premiere migration

dotnet ef migrations add InitialCreate --project src/Catalog.API/Catalog.API.csproj

# redis

## install

dotnet new webapi -n Basket.API -o src/Basket.API --no-openapi
dotnet sln add src/Basket.API/Basket.API.csproj
dotnet add src/Basket.API/Basket.API.csproj reference src/eShop.ServiceDefaults/eShop.ServiceDefaults.csproj
dotnet add src/eShop.AppHost/eShop.AppHost.csproj reference src/Basket.API/Basket.API.csproj

dotnet add src/Basket.API/Basket.API.csproj package Aspire.StackExchange.Redis

dotnet add src/eShop.AppHost/eShop.AppHost.csproj package Aspire.Hosting.Redis

# RabbitMQ

## install

dotnet add src/eShop.AppHost/eShop.AppHost.csproj package Aspire.Hosting.RabbitMQ
dotnet add src/Basket.API/Basket.API.csproj package RabbitMQ.Client

## Crée le dossier des événements partagés

dotnet new classlib -n eShop.IntegrationEvents -o src/eShop.IntegrationEvents
dotnet sln add src/eShop.IntegrationEvents/eShop.IntegrationEvents.csproj
dotnet add src/Basket.API/Basket.API.csproj reference src/eShop.IntegrationEvents/eShop.IntegrationEvents.csproj

## password

docker exec $(docker ps -qf "name=rabbitmq") env | grep RABBITMQ

# DDD

## install

dotnet new webapi -n Ordering.API -o src/Ordering.API --no-openapi
dotnet sln add src/Ordering.API/Ordering.API.csproj
dotnet add src/Ordering.API/Ordering.API.csproj reference src/eShop.ServiceDefaults/eShop.ServiceDefaults.csproj
dotnet add src/Ordering.API/Ordering.API.csproj reference src/eShop.IntegrationEvents/eShop.IntegrationEvents.csproj
dotnet add src/eShop.AppHost/eShop.AppHost.csproj reference src/Ordering.API/Ordering.API.csproj

### packages

dotnet add src/Ordering.API/Ordering.API.csproj package Microsoft.EntityFrameworkCore
dotnet add src/Ordering.API/Ordering.API.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Ordering.API/Ordering.API.csproj package Microsoft.EntityFrameworkCore.Design
dotnet add src/Ordering.API/Ordering.API.csproj package Aspire.Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Ordering.API/Ordering.API.csproj package RabbitMQ.Client

## structure

mkdir -p src/Ordering.API/Domain/AggregatesModel/OrderAggregate
mkdir -p src/Ordering.API/Domain/Events
mkdir -p src/Ordering.API/Domain/SeedWork
mkdir -p src/Ordering.API/Infrastructure/Repositories
mkdir -p src/Ordering.API/Application/Commands
mkdir -p src/Ordering.API/Application/Queries

## MediatR

dotnet add src/Ordering.API/Ordering.API.csproj package MediatR

# CQRS

dotnet ef migrations add InitialCreate --project src/Ordering.API/Ordering.API.csproj

## call test

curl -X POST https://localhost:7225/api/basket \
 -H "Content-Type: application/json" \
 -k \
 -d '{
"buyerId": "user1",
"items": [{
"productId": 1,
"productName": ".NET Bot Black Sweatshirt",
"unitPrice": 19.5,
"quantity": 2
}]
}'

curl -X POST https://localhost:7225/api/basket/checkout \
 -H "Content-Type: application/json" \
 -k \
 -d '{
"buyerId": "user1",
"city": "Brussels",
"street": "Rue de la Loi 1",
"country": "Belgium",
"zipCode": "1000",
"cardNumber": "4111111111111111",
"cardHolderName": "Michel",
"cardExpiration": "2027-01-01"
}'

curl -k https://localhost:7102/api/orders/user1

# Identity.API

## install

dotnet new webapi -n Identity.API -o src/Identity.API --no-openapi
dotnet sln add src/Identity.API/Identity.API.csproj
dotnet add src/Identity.API/Identity.API.csproj reference src/eShop.ServiceDefaults/eShop.ServiceDefaults.csproj
dotnet add src/eShop.AppHost/eShop.AppHost.csproj reference src/Identity.API/Identity.API.csproj

### packages

dotnet add src/Identity.API/Identity.API.csproj package Duende.IdentityServer
dotnet add src/Identity.API/Identity.API.csproj package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet add src/Identity.API/Identity.API.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Identity.API/Identity.API.csproj package Aspire.Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Identity.API/Identity.API.csproj package Duende.IdentityServer.AspNetIdentity

dotnet add src/Catalog.API/Catalog.API.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/Basket.API/Basket.API.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/Ordering.API/Ordering.API.csproj package Microsoft.AspNetCore.Authentication.JwtBearer

## migration

dotnet add src/Identity.API/Identity.API.csproj package Microsoft.EntityFrameworkCore.Design
dotnet ef migrations add InitialCreate --project src/Identity.API/Identity.API.csproj

## add page

dotnet add src/Identity.API/Identity.API.csproj package Duende.IdentityServer.AspNetIdentity

## check

https://localhost:7267/.well-known/openid-configuration

# Frontend Blazor WebAssembly

## install

dotnet new blazorwasm -n WebApp -o src/WebApp
dotnet sln add src/WebApp/WebApp.csproj
dotnet add src/eShop.AppHost/eShop.AppHost.csproj reference src/WebApp/WebApp.csproj
dotnet add src/WebApp/WebApp.csproj package Microsoft.AspNetCore.Components.WebAssembly.Server

### packages

dotnet add src/WebApp/WebApp.csproj package Microsoft.AspNetCore.Components.WebAssembly.Authentication
dotnet add src/WebApp/WebApp.csproj package Microsoft.Extensions.Http

# serveur pour héberger WebApp

## install

dotnet new web -n WebApp.Server -o src/WebApp.Server
dotnet sln add src/WebApp.Server/WebApp.Server.csproj
dotnet add src/WebApp.Server/WebApp.Server.csproj reference src/WebApp/WebApp.csproj
dotnet add src/WebApp.Server/WebApp.Server.csproj reference src/eShop.ServiceDefaults/eShop.ServiceDefaults.csproj

### packages

dotnet add src/WebApp.Server/WebApp.Server.csproj package Microsoft.AspNetCore.Components.WebAssembly.Server

dotnet remove src/eShop.AppHost/eShop.AppHost.csproj reference src/WebApp/WebApp.csproj
dotnet add src/eShop.AppHost/eShop.AppHost.csproj reference src/WebApp.Server/WebApp.Server.csproj
# eShop-
