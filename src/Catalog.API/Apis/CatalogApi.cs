using Catalog.API.Data;
using Catalog.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Apis;

// ============================================================================
// FICHIER : CatalogApi.cs  —  les ENDPOINTS HTTP du catalogue.
//
// CONCEPT : « minimal API ». C'est le style léger d'ASP.NET Core où l'on déclare
//   une route et son traitement par un simple appel (app.MapGet("/...", handler))
//   au lieu d'écrire une classe Controller + des attributs. Le handler est une
//   lambda ; ses paramètres sont fournis AUTOMATIQUEMENT par l'injection de
//   dépendances (DI) ou liés depuis la route / le corps de la requête.
//
// PATTERN ICI : on regroupe tous les endpoints dans une MÉTHODE D'EXTENSION
//   (MapCatalogApi) appelée une fois dans Program.cs (app.MapCatalogApi()). Cela
//   garde Program.cs lisible et range les routes par domaine.
//
// SÉCURITÉ — le POURQUOI (concept central de ce service) :
//   - La LECTURE (GET) est PUBLIQUE : n'importe qui, même non connecté, peut
//     consulter le catalogue. C'est une vitrine ; pas de raison de la protéger.
//   - L'ÉCRITURE (POST/PUT/DELETE) est RÉSERVÉE aux administrateurs, via
//     l'attribut [Authorize(Roles = "Admin")]. On ne veut pas qu'un client
//     lambda modifie les produits.
//   Cette asymétrie lecture-publique / écriture-restreinte est le point
//   pédagogique majeur du fichier. Le câblage de l'authentification (validation
//   du jeton JWT) se fait dans Program.cs ; ici on ne fait qu'EXIGER un rôle.
//
// À LIRE après CatalogContextSeed.cs, avant Program.cs. Pour comprendre d'OÙ
//   vient le rôle "Admin", enchaînez ensuite sur Identity.API (Config.cs +
//   CustomProfileService.cs + ApplicationDbContextSeed.cs).
// ============================================================================
public static class CatalogApi
{
    public static RouteGroupBuilder MapCatalogApi(this IEndpointRouteBuilder app)
    {
        // MapGroup : préfixe commun appliqué à toutes les routes ci-dessous.
        // Toutes deviennent /api/catalog/... (on évite de répéter le préfixe).
        var group = app.MapGroup("/api/catalog");

        // GET — accessible à tous (même non connecté)
        group.MapGet("/items", async (CatalogDbContext db) =>
        {
            // Le DbContext est injecté automatiquement par le conteneur DI dans la lambda.
            // Include charge les navigations marque/type (jointures) pour les renvoyer
            // dans la réponse ; sinon elles seraient null (pas de lazy loading ici).
            var items = await db.CatalogItems
                .Include(i => i.CatalogBrand)
                .Include(i => i.CatalogType)
                .ToListAsync();
            return Results.Ok(items);
        });

        // GET d'un produit par son Id ("{id:int}" contraint le paramètre de route à un entier).
        group.MapGet("/items/{id:int}", async (int id, CatalogDbContext db) =>
        {
            var item = await db.CatalogItems
                .Include(i => i.CatalogBrand)
                .Include(i => i.CatalogType)
                .FirstOrDefaultAsync(i => i.Id == id);
            // 404 si introuvable, sinon 200 + le produit.
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        // POST/PUT/DELETE — Admin seulement.
        // [Authorize(Roles = "Admin")] = ce endpoint exige (1) un jeton valide ET
        // (2) que ce jeton porte un claim "role" valant "Admin".
        //   - Un "claim" est une information attestée par le serveur d'identité et
        //     transportée dans le jeton (ex. l'identité de l'utilisateur, ses rôles).
        //   - Le claim "role" est rempli côté Identity.API par CustomProfileService.
        // Parmi les utilisateurs seedés, seule "alice" a le rôle "Admin" (bob ne l'a
        // pas) : seul son jeton franchira cette barrière.
        group.MapPost("/items", [Authorize(Roles = "Admin")] async (CatalogItem item, CatalogDbContext db) =>
        {
            db.CatalogItems.Add(item);
            await db.SaveChangesAsync();
            // 201 Created + en-tête Location pointant vers la ressource nouvellement créée.
            return Results.Created($"/api/catalog/items/{item.Id}", item);
        });

        // PUT — mise à jour complète d'un produit existant (Admin seulement).
        group.MapPut("/items/{id:int}", [Authorize(Roles = "Admin")] async (int id, CatalogItem item, CatalogDbContext db) =>
        {
            // On recharge l'entité suivie par EF puis on recopie les champs : ainsi
            // le change tracker génère un UPDATE ciblé au SaveChanges.
            var existing = await db.CatalogItems.FindAsync(id);
            if (existing is null) return Results.NotFound();

            existing.Name = item.Name;
            existing.Description = item.Description;
            existing.Price = item.Price;
            existing.AvailableStock = item.AvailableStock;
            existing.CatalogBrandId = item.CatalogBrandId;
            existing.CatalogTypeId = item.CatalogTypeId;

            await db.SaveChangesAsync();
            return Results.Ok(existing);
        });

        // DELETE — suppression d'un produit (Admin seulement).
        group.MapDelete("/items/{id:int}", [Authorize(Roles = "Admin")] async (int id, CatalogDbContext db) =>
        {
            var item = await db.CatalogItems.FindAsync(id);
            if (item is null) return Results.NotFound();
            db.CatalogItems.Remove(item);
            await db.SaveChangesAsync();
            // 204 No Content : succès sans corps de réponse.
            return Results.NoContent();
        });

        // Endpoints pour brands et types (Admin)
        // Note : ces GET de référence sont en réalité publics (pas de [Authorize]),
        // comme les GET de produits ; ils servent à alimenter les listes déroulantes du front.
        group.MapGet("/brands", async (CatalogDbContext db) =>
            Results.Ok(await db.CatalogBrands.ToListAsync()));

        group.MapGet("/types", async (CatalogDbContext db) =>
            Results.Ok(await db.CatalogTypes.ToListAsync()));

        // On renvoie le groupe pour permettre un éventuel chaînage côté appelant.
        return group;
    }
}