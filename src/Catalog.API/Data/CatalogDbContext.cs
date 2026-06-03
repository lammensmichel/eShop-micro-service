using Catalog.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Data;

// DbContext = unité de travail + point d'accès à la base (ici Postgres "catalogdb").
// Il dérive directement de DbContext : pas de logique de domaine, c'est un service
// d'infrastructure de persistance pur.
public class CatalogDbContext : DbContext
{
    // Les options (chaîne de connexion, provider Npgsql, ...) sont injectées par le
    // conteneur DI ; elles sont configurées dans Program.cs via AddNpgsqlDbContext.
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options)
    {
    }

    // Chaque DbSet expose une table. La syntaxe "=> Set<T>()" (au lieu d'une propriété
    // auto avec setter) évite l'avertissement nullable et reste en lecture seule.
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<CatalogBrand> CatalogBrands => Set<CatalogBrand>();
    public DbSet<CatalogType> CatalogTypes => Set<CatalogType>();

    // OnModelCreating configure le mapping objet/relationnel via la Fluent API.
    // Ces règles sont prises en compte lors de la génération des migrations.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Le prix est un montant monétaire : on impose un type SQL precise
        // (18 chiffres dont 2 décimales) plutôt que le decimal par défaut du provider.
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