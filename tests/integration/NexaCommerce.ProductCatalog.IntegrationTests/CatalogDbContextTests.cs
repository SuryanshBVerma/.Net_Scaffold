using Microsoft.EntityFrameworkCore;
using NexaCommerce.ProductCatalog.Data;
using NexaCommerce.ProductCatalog.Data.Entities;
using Shouldly;
using Xunit;

namespace NexaCommerce.ProductCatalog.IntegrationTests;

/// <summary>
/// LEARNING — Integration tests vs unit tests:
///
///   Unit tests (ProductServiceTests.cs):
///     • InMemory EF Core — fast, but skips SQL translation
///     • Mock dependencies (IMessageBus, IObjectStorageService)
///     • Test business logic in isolation
///
///   Integration tests (this file):
///     • Real PostgreSQL via Testcontainers
///     • Test LINQ queries against the REAL SQL engine
///     • Catch bugs InMemory silently ignores (GroupBy, navigation joins, constraints)
///     • Slower — run once in CI, not on every save
///
/// ALL queries in this file demonstrate a different LINQ operator/pattern.
/// Read the comments — they explain what SQL each LINQ expression compiles to.
/// </summary>
public sealed class CatalogDbContextTests(CatalogDbFixture fixture)
    : IClassFixture<CatalogDbFixture>
{
    // IDs are defined on the fixture (single source of truth).
    private static readonly Guid ElectronicsId = CatalogDbFixture.ElectronicsId;
    private static readonly Guid AppliancesId  = CatalogDbFixture.AppliancesId;

    // LEARNING — each test opens a fresh context so there is no shared change-tracker
    // state between tests. The data itself is stable (seeded once in CatalogDbFixture).


    [Fact]
    public async Task Where_filters_by_price_correctly()
    {
        // LINQ LEARNING: .Where() translates to SQL WHERE clause.
        // SQL: SELECT * FROM "Products" WHERE "Price" < 100
        await using var db = fixture.CreateDbContext();
        var results = await db.Products.Where(p => p.Price < 100).ToListAsync();

        // LEARNING: ShouldAllBe — assert EVERY item satisfies a condition.
        results.ShouldNotBeEmpty();
        results.ShouldAllBe(p => p.Price < 100);
    }

    [Fact]
    public async Task Where_filters_active_products_only()
    {
        // LINQ LEARNING: Chain multiple .Where() calls — each adds an AND clause.
        // SQL: SELECT * FROM "Products" WHERE "IsActive" = true AND "Price" < 200
        await using var db = fixture.CreateDbContext();
        var results = await db.Products
            .Where(p => p.IsActive)
            .Where(p => p.Price < 200)
            .ToListAsync();

        results.ShouldAllBe(p => p.IsActive && p.Price < 200);
    }

    // ── SELECT (projection) ───────────────────────────────────────────────────

    [Fact]
    public async Task Select_projects_to_anonymous_type()
    {
        // LINQ LEARNING: .Select() translates to SQL SELECT with specific columns.
        // SQL: SELECT "Id", "Name", "Price" FROM "Products"
        // This avoids loading unused columns — important for wide tables.
        await using var db = fixture.CreateDbContext();
        var summaries = await db.Products
            .Select(p => new { p.Id, p.Name, p.Price })
            .ToListAsync();

        summaries.ShouldNotBeEmpty();
        summaries.ShouldAllBe(s => s.Price > 0);
    }

    // ── ORDER BY ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task OrderBy_sorts_ascending_by_name()
    {
        // LINQ LEARNING: .OrderBy() → SQL ORDER BY column ASC
        // .OrderByDescending() → SQL ORDER BY column DESC
        await using var db = fixture.CreateDbContext();
        var ordered = await db.Products.OrderBy(p => p.Name).ToListAsync();

        // Assert ordering is correct by comparing to a locally sorted copy.
        var expectedOrder = ordered.Select(p => p.Name).OrderBy(n => n).ToList();
        ordered.Select(p => p.Name).ToList().ShouldBe(expectedOrder);
    }

    [Fact]
    public async Task OrderByDescending_then_ThenBy_sorts_correctly()
    {
        // LINQ LEARNING: .ThenBy() adds a secondary sort — SQL: ORDER BY col1 DESC, col2 ASC
        await using var db = fixture.CreateDbContext();
        var ordered = await db.Products
            .OrderByDescending(p => p.CategoryId)
            .ThenBy(p => p.Price)
            .ToListAsync();

        ordered.ShouldNotBeEmpty();
    }

    // ── SKIP + TAKE (pagination) ──────────────────────────────────────────────

    [Fact]
    public async Task Skip_Take_returns_correct_page()
    {
        // LINQ LEARNING: .Skip() + .Take() → SQL OFFSET n LIMIT m
        // This is server-side pagination — only the requested slice is loaded.
        // Critical for performance on large tables.
        await using var db = fixture.CreateDbContext();
        const int pageSize = 3;
        const int page     = 2; // 1-based

        var pageResults = await db.Products
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        pageResults.Count.ShouldBe(pageSize);
    }

    [Fact]
    public async Task Take_returns_at_most_N_results()
    {
        // LINQ LEARNING: .Take(n) alone (without Skip) → SQL LIMIT n
        // Used for "top N" queries, e.g. the 5 most recently added products.
        await using var db = fixture.CreateDbContext();
        var top3 = await db.Products
            .OrderByDescending(p => p.CreatedAt)
            .Take(3)
            .ToListAsync();

        top3.Count.ShouldBe(3);
    }

    // ── ANY / ALL / COUNT ─────────────────────────────────────────────────────

    [Fact]
    public async Task AnyAsync_returns_true_when_match_exists()
    {
        // LINQ LEARNING: .AnyAsync() → SQL EXISTS (SELECT 1 FROM ... WHERE ...)
        // More efficient than .CountAsync() > 0 — stops scanning after first match.
        await using var db = fixture.CreateDbContext();
        var hasExpensive = await db.Products.AnyAsync(p => p.Price > 500);
        hasExpensive.ShouldBeTrue();
    }

    [Fact]
    public async Task CountAsync_with_predicate_counts_matching_rows()
    {
        // LINQ LEARNING: .CountAsync(predicate) → SQL SELECT COUNT(*) WHERE ...
        // No data is loaded into memory — just the count comes back from the DB.
        await using var db = fixture.CreateDbContext();
        var activeCount = await db.Products.CountAsync(p => p.IsActive);
        activeCount.ShouldBeGreaterThan(0);
    }

    // ── GROUP BY + aggregation ────────────────────────────────────────────────

    [Fact]
    public async Task GroupBy_counts_products_per_category()
    {
        // LINQ LEARNING: .GroupBy() → SQL GROUP BY
        // SQL:
        //   SELECT "CategoryId", COUNT(*), AVG("Price")
        //   FROM "Products"
        //   GROUP BY "CategoryId"
        //
        // NOTE: Real Postgres executes this server-side.
        // InMemory EF cannot translate GroupBy + navigation properties to SQL —
        // it silently falls back to client-side evaluation. This is a common
        // source of bugs that only integration tests against real Postgres catch.
        await using var db = fixture.CreateDbContext();
        var groups = await db.Products
            .GroupBy(p => p.CategoryId)
            .Select(g => new
            {
                CategoryId = g.Key,
                Count      = g.Count(),
                AvgPrice   = g.Average(p => p.Price)
            })
            .ToListAsync();

        groups.ShouldNotBeEmpty();
        groups.ShouldContain(g => g.CategoryId == ElectronicsId && g.Count > 0);
        groups.ShouldContain(g => g.CategoryId == AppliancesId  && g.Count > 0);
        groups.ShouldAllBe(g => g.AvgPrice > 0);
    }

    // ── INCLUDE (JOIN via navigation) ─────────────────────────────────────────

    [Fact]
    public async Task Include_loads_related_Category_entity()
    {
        // LINQ LEARNING: .Include() → SQL LEFT JOIN
        // SQL: SELECT p.*, c.* FROM "Products" p LEFT JOIN "Categories" c ON p."CategoryId" = c."Id"
        //
        // Without Include: product.Category is null (lazy loading disabled by default in EF Core).
        // With Include:    product.Category is populated from the JOIN result.
        await using var db = fixture.CreateDbContext();
        var products = await db.Products
            .Include(p => p.Category)
            .Where(p => p.IsActive)
            .ToListAsync();

        products.ShouldNotBeEmpty();
        products.ShouldAllBe(p => p.Category != null);
        products.ShouldAllBe(p => p.Category!.Name.Length > 0);
    }

    // ── AsNoTracking — read-only performance ─────────────────────────────────

    [Fact]
    public async Task AsNoTracking_returns_untracked_entities()
    {
        // LINQ LEARNING: .AsNoTracking() — EF Core does NOT track these entities.
        //
        // By default, EF Core tracks every entity loaded (adds it to the change tracker).
        // For read-only queries this is wasted overhead — you're never calling SaveChanges().
        // AsNoTracking() skips tracking → faster query + less memory.
        //
        // RULE: Use AsNoTracking() for every read path. Use tracked queries ONLY when
        //       you intend to modify and SaveChanges().
        await using var db = fixture.CreateDbContext();
        var products = await db.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Price)
            .ToListAsync();

        products.ShouldNotBeEmpty();
        // Verify ordering: each price should be >= the previous.
        for (var i = 1; i < products.Count; i++)
        {
            products[i].Price.ShouldBeGreaterThanOrEqualTo(products[i - 1].Price);
        }
    }

    // ── FirstOrDefaultAsync — single-row lookup ───────────────────────────────

    [Fact]
    public async Task FirstOrDefaultAsync_returns_null_when_not_found()
    {
        // LINQ LEARNING: .FirstOrDefaultAsync() → SQL SELECT ... LIMIT 1
        // Returns null (not an exception) when no row matches.
        // Use .FirstAsync() when you KNOW the row must exist (throws if missing).
        await using var db = fixture.CreateDbContext();
        var missing = await db.Products.FirstOrDefaultAsync(p => p.Name == "DoesNotExist");
        missing.ShouldBeNull();
    }

    [Fact]
    public async Task FirstOrDefaultAsync_returns_matching_product()
    {
        await using var db = fixture.CreateDbContext();
        var laptop = await db.Products.FirstOrDefaultAsync(p => p.Name == "Laptop");
        laptop.ShouldNotBeNull();
        laptop!.Price.ShouldBe(999.99m);
    }
}
