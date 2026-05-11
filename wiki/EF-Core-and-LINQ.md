# EF Core & LINQ

## DbContext Design

`CatalogDbContext` is the **Unit of Work** for the ProductCatalog bounded context. It exposes two `DbSet` properties and configures the schema via Fluent API:

```csharp
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DbContext(options)
{
    public DbSet<Product>  Products   => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Price).HasPrecision(18, 4);  // money: always specify precision

            entity.HasIndex(p => p.CategoryId);                          // FK index
            entity.HasIndex(p => new { p.IsActive, p.CreatedAt });       // composite for cleanup job

            entity.HasOne(p => p.Category)
                  .WithMany(c => c.Products)
                  .HasForeignKey(p => p.CategoryId)
                  .OnDelete(DeleteBehavior.Restrict);  // prevent accidental cascade delete
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(c => c.Name).IsUnique();  // no duplicate category names
        });
    }
}
```

---

## LINQ Operators Demonstrated

All operators below are tested in `CatalogDbContextTests.cs` against a **real PostgreSQL** instance via Testcontainers.

### WHERE — Filter rows
```csharp
// SQL: SELECT * FROM "Products" WHERE "IsActive" = true
var active = await db.Products
    .Where(p => p.IsActive)
    .ToListAsync();
```

### SELECT — Project columns
```csharp
// SQL: SELECT "Name", "Price" FROM "Products"
var prices = await db.Products
    .Select(p => new { p.Name, p.Price })
    .ToListAsync();
```

### ORDER BY / THEN BY
```csharp
// SQL: ORDER BY "Price" DESC, "Name" ASC
var sorted = await db.Products
    .OrderByDescending(p => p.Price)
    .ThenBy(p => p.Name)
    .ToListAsync();
```

### SKIP / TAKE — Pagination
```csharp
// SQL: OFFSET 10 LIMIT 5
var page = await db.Products
    .OrderBy(p => p.CreatedAt)
    .Skip(pageIndex * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

### ANY — Existence check (no full scan)
```csharp
// SQL: SELECT EXISTS (SELECT 1 FROM "Products" WHERE "CategoryId" = @id)
bool hasProducts = await db.Products
    .AnyAsync(p => p.CategoryId == categoryId);
```

### COUNT — Aggregation
```csharp
// SQL: SELECT COUNT(*) FROM "Products" WHERE "IsActive" = true
int activeCount = await db.Products.CountAsync(p => p.IsActive);
```

### GROUP BY — Aggregate per group
```csharp
// SQL: SELECT c."Name", COUNT(p."Id") FROM "Products" p
//      JOIN "Categories" c ON p."CategoryId" = c."Id"
//      GROUP BY c."Name"
var stats = await db.Products
    .GroupBy(p => p.Category!.Name)
    .Select(g => new { Category = g.Key, Count = g.Count() })
    .OrderByDescending(x => x.Count)
    .ToListAsync();
```

> **Why test GROUP BY against real Postgres?**
> EF Core's InMemory provider executes GROUP BY as client-side LINQ — it never sends a `GROUP BY` clause to SQL. A query that works with InMemory may fail or perform catastrophically on a real database. Testcontainers catches this.

### INCLUDE — Eager loading (JOIN)
```csharp
// SQL: SELECT p.*, c.* FROM "Products" p
//      LEFT JOIN "Categories" c ON p."CategoryId" = c."Id"
var withCategory = await db.Products
    .Include(p => p.Category)
    .ToListAsync();
```

### AS NO TRACKING — Read-only performance
```csharp
// EF Core skips change-tracking overhead — use for read-only queries
var readOnly = await db.Products
    .AsNoTracking()
    .Where(p => p.IsActive)
    .ToListAsync();
```

### FIRST OR DEFAULT — Single result with null safety
```csharp
// Returns null if not found — never throws
var product = await db.Products
    .FirstOrDefaultAsync(p => p.Id == id);
```

---

## Load-then-Mutate Pattern

EF Core's InMemory provider (used in unit tests) does not support direct SQL `UPDATE`. To keep unit and integration tests consistent, always **load the entity first**, then mutate:

```csharp
// ✅ Works with both InMemory (unit tests) and real Postgres (integration tests)
var product = await db.Products.FindAsync(id);
if (product is null) return Result.NotFound();

product.Name  = request.Name;
product.Price = request.Price;
await db.SaveChangesAsync();

// ❌ Fails with InMemory provider — never do this in shared business logic
await db.Products
    .Where(p => p.Id == id)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, request.Name));
```

---

## Migrations vs EnsureCreated

| Method | When to use |
|---|---|
| `MigrateAsync()` | Production and staging — applies versioned migration files |
| `EnsureCreatedAsync()` | Tests only — creates schema from the EF model snapshot, no migration files |

`MigrateDatabase<T>()` in SharedKernel detects the provider:
```csharp
// InMemory (tests) → EnsureCreatedAsync
// Real DB (prod)   → MigrateAsync
if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
    await db.Database.EnsureCreatedAsync();
else
    await db.Database.MigrateAsync();
```

---

## HasData Seeding and Tests

`OnModelCreating` uses `HasData()` to seed reference data at migration time:

```csharp
modelBuilder.Entity<Category>().HasData(
    new Category { Id = electronicsId, Name = "Electronics" },
    new Category { Id = apparelId,     Name = "Apparel" }
);
```

**Gotcha in integration tests:** `EnsureCreatedAsync()` applies `HasData` seeds. If your test fixture then tries to insert a `Category { Name = "Electronics" }`, it violates the `UNIQUE` index on `Category.Name`.

**Fix:** clear HasData rows before seeding test data:
```csharp
// In CatalogDbFixture.SeedAsync()
await db.Products.ExecuteDeleteAsync();    // FK: delete products first
await db.Categories.ExecuteDeleteAsync();  // then categories
// Now insert your controlled test dataset
```
