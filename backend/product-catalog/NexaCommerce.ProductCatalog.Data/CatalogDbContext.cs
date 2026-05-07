using Microsoft.EntityFrameworkCore;
using NexaCommerce.ProductCatalog.Data.Entities;

namespace NexaCommerce.ProductCatalog.Data;

/// <summary>
/// EF Core DbContext for the ProductCatalog bounded context.
///
/// LEARNING — DbContext responsibilities:
///   1. Exposes DbSet properties → these become LINQ queryable tables.
///   2. OnModelCreating() → Fluent API configuration (constraints, indexes, seed data).
///   3. SaveChangesAsync() → wraps everything in a single database transaction.
///
/// LEARNING — Aspire connection string injection:
///   In production (via Aspire), the connection string is injected automatically
///   as environment variable:  ConnectionStrings__catalog-db
///   The Aspire client integration (Aspire.Npgsql.EntityFrameworkCore.PostgreSQL)
///   picks it up and configures the DbContext — zero manual config in appsettings.
///
/// LEARNING — Unit of Work pattern:
///   CatalogDbContext IS the unit of work. All repository operations within
///   one request share the same context instance (Scoped lifetime), so
///   SaveChangesAsync() commits everything atomically.
/// </summary>
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    // LEARNING: DbSet<T> exposes the entity as a LINQ queryable.
    // db.Products is equivalent to SELECT * FROM "Products" in SQL.
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Products table ────────────────────────────────────────────────
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(p => p.Description)
                .HasMaxLength(2000);

            // LEARNING: decimal precision for money — always specify scale.
            // Default decimal mapping in Postgres is numeric(18,2).
            // 18 digits total, 4 decimal places — supports most currency scenarios.
            entity.Property(p => p.Price)
                .HasPrecision(18, 4);

            entity.Property(p => p.ImageKey)
                .HasMaxLength(500);

            // Index on CategoryId — speeds up "list products by category" queries.
            entity.HasIndex(p => p.CategoryId);

            // Index on IsActive + CreatedAt — the cleanup job queries this combo.
            entity.HasIndex(p => new { p.IsActive, p.CreatedAt });

            // Foreign key: Product.CategoryId → Category.Id
            // LEARNING: HasOne/WithMany defines the relationship.
            // OnDelete(Restrict) prevents accidentally deleting a category that has products.
            entity.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Categories table ──────────────────────────────────────────────
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(100);

            // Unique index: no two categories can share the same name.
            entity.HasIndex(c => c.Name).IsUnique();
        });

        // ── Seed data ─────────────────────────────────────────────────────
        // LEARNING: HasData() seeds rows at migration time.
        // Use deterministic GUIDs so re-running migrations doesn't duplicate them.
        // These are development/learning seeds — remove or replace for production.
        var electronicsId = new Guid("10000000-0000-0000-0000-000000000001");
        var apparelId     = new Guid("10000000-0000-0000-0000-000000000002");
        var homeId        = new Guid("10000000-0000-0000-0000-000000000003");

        modelBuilder.Entity<Category>().HasData(
            new Category { Id = electronicsId, Name = "Electronics",  Description = "Gadgets and devices" },
            new Category { Id = apparelId,     Name = "Apparel",      Description = "Clothing and accessories" },
            new Category { Id = homeId,        Name = "Home & Garden",Description = "Furniture and garden tools" }
        );

        modelBuilder.Entity<Product>().HasData(
            new Product
            {
                Id = new Guid("20000000-0000-0000-0000-000000000001"),
                Name = "Wireless Keyboard",
                Description = "Compact Bluetooth keyboard with 12-month battery life.",
                Price = 79.99m,
                CategoryId = electronicsId,
                IsActive = true,
                CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
            },
            new Product
            {
                Id = new Guid("20000000-0000-0000-0000-000000000002"),
                Name = "Ergonomic Office Chair",
                Description = "Lumbar support, adjustable armrests, 5-year warranty.",
                Price = 349.00m,
                CategoryId = homeId,
                IsActive = true,
                CreatedAt = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero)
            }
        );
    }
}
