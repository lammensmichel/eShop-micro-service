using Catalog.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Data;

// ============================================================================
// FICHIER : CatalogDbContext.cs  —  la PORTE D'ENTRÉE vers la base de données.
//
// RÔLE : c'est par cet objet que tout le code parle à Postgres (base "catalogdb").
//
// CONCEPTS ILLUSTRÉS (jargon EF Core défini à sa 1re apparition) :
//   - DbContext : classe de base d'EF Core qui représente une SESSION avec la
//     base. Elle joue deux rôles : (1) « unité de travail » — elle suit en
//     mémoire les objets chargés/modifiés (le « change tracker ») et, au moment
//     de SaveChanges, génère en un lot les INSERT/UPDATE/DELETE correspondants ;
//     (2) point d'accès aux tables via ses DbSet.
//   - DbSet<T> : représente UNE table (ici, une par type de modèle). On y écrit
//     les requêtes LINQ (Where, Include, ToListAsync...) qu'EF traduit en SQL.
//   - Fluent API : façon de configurer le MAPPING objet/relationnel par du code
//     (modelBuilder.Entity<...>()...), au lieu d'annotations sur les classes.
//     « Mapping » = la correspondance classe<->table, propriété<->colonne, et
//     les contraintes (types SQL, index, relations).
//   - Migration : un fichier généré (dossier Migrations/, hors de notre champ)
//     qui décrit les changements de schéma déduits du modèle. Appliquée au
//     démarrage par db.Database.MigrateAsync() (voir Program.cs), elle crée/met
//     à jour les tables réelles. Modifier OnModelCreating => regénérer une migration.
//
// Ce DbContext dérive DIRECTEMENT de DbContext : aucune logique de domaine, c'est
// un pur service d'infrastructure de persistance (cohérent avec le modèle anémique).
//
// À LIRE après les Models/ et avant CatalogContextSeed.cs.
// ============================================================================
public class CatalogDbContext : DbContext
{
    // Les "options" (chaîne de connexion, choix du provider Npgsql pour Postgres,
    // etc.) ne sont PAS codées ici : elles sont injectées par le conteneur
    // d'injection de dépendances (DI). Elles sont configurées dans Program.cs via
    // builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb").
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options)
    {
    }

    // Un DbSet par table. La syntaxe "=> Set<T>()" (propriété en lecture seule,
    // au lieu d'une propriété auto "{ get; set; }") évite l'avertissement nullable
    // tout en laissant EF fournir l'instance réelle.
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<CatalogBrand> CatalogBrands => Set<CatalogBrand>();
    public DbSet<CatalogType> CatalogTypes => Set<CatalogType>();

    // OnModelCreating : point d'extension où EF construit son modèle interne. C'est
    // ici qu'on affine le mapping via la Fluent API. Ces règles sont « gelées »
    // dans la prochaine migration générée (elles définissent le schéma SQL).
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Le prix est un montant monétaire : on impose un type SQL précis
        // decimal(18,2) (18 chiffres dont 2 décimales) plutôt que le decimal par
        // défaut du provider, pour éviter toute perte de précision sur l'argent.
        modelBuilder.Entity<CatalogItem>()
            .Property(c => c.Price)
            .HasColumnType("decimal(18,2)");

        // Index unique sur le nom de marque : empêche deux marques homonymes.
        modelBuilder.Entity<CatalogBrand>()
            .HasIndex(b => b.Name)
            .IsUnique();

        // Index unique sur le libellé de type : empêche deux catégories homonymes.
        modelBuilder.Entity<CatalogType>()
            .HasIndex(t => t.Type)
            .IsUnique();
    }
}