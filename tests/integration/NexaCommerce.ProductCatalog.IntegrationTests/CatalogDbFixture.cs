using Microsoft.EntityFrameworkCore;
using NexaCommerce.IntegrationTests.Common.Fixtures;
using NexaCommerce.ProductCatalog.Data;
using NexaCommerce.ProductCatalog.Data.Entities;
using Xunit;

namespace NexaCommerce.ProductCatalog.IntegrationTests;

/// <summary>
/// LEARNING — Why a dedicated seeding fixture:
///
///   xunit.v3 runs tests within the same class in parallel on multi-core machines
///   (GitHub Actions runners have 2+ CPUs). The original SeedAsync() checked
///   "if no data exists → seed" inside EACH test. When multiple tests ran that
///   check simultaneously, they all saw an empty DB (before any commit), then all
///   tried to INSERT the same deterministic GUIDs → primary key violations and
///   flaky failures.
///
///   Solution: seed ONCE inside InitializeAsync() — which xunit.v3 guarantees runs
///   before the first test. Each test then just opens a fresh read-only context.
///   No race condition possible.
///
///   This pattern (fixture-level seeding) is the standard approach for integration
///   test fixtures that share state across a test class.
/// </summary>
public sealed class CatalogDbFixture : IAsyncLifetime
{
    // Wrap the common infrastructure fixture rather than duplicating container setup.
    private readonly PostgreSqlFixture _postgres = new();

    // Deterministic IDs — exposed so test assertions reference the same values.
    public static readonly Guid ElectronicsId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    public static readonly Guid AppliancesId  = Guid.Parse("a0000000-0000-0000-0000-000000000002");

    public CatalogDbContext CreateDbContext() => _postgres.CreateDbContext();

    /// <summary>
    /// LEARNING — IAsyncLifetime.InitializeAsync():
    ///   Runs ONCE before the first test in the class. At this point there is
    ///   no parallelism — xunit.v3 guarantees fixture init is sequential.
    ///   We start the Postgres container AND seed the data here so every test
    ///   starts with a stable, known dataset.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        await SeedAsync();
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        // EnsureCreatedAsync() applies HasData() seeds from OnModelCreating(),
        // which already inserts "Electronics", "Apparel", "Home & Garden" categories.
        // Category.Name has a unique index, so inserting "Electronics" again would
        // violate the constraint. Clear HasData rows first so the fixture controls
        // the exact dataset each test relies on.
        await db.Products.ExecuteDeleteAsync();
        await db.Categories.ExecuteDeleteAsync();

        db.Categories.AddRange(
            new Category { Id = ElectronicsId, Name = "Electronics" },
            new Category { Id = AppliancesId,  Name = "Appliances"  });

        db.Products.AddRange(
            new Product { Id = Guid.NewGuid(), Name = "Laptop",       Price = 999.99m,  CategoryId = ElectronicsId, IsActive = true,  CreatedAt = DateTimeOffset.UtcNow.AddDays(-10), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-10), Description = "High-end laptop" },
            new Product { Id = Guid.NewGuid(), Name = "Headphones",   Price = 49.99m,   CategoryId = ElectronicsId, IsActive = true,  CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),  UpdatedAt = DateTimeOffset.UtcNow.AddDays(-5),  Description = "Wireless headphones" },
            new Product { Id = Guid.NewGuid(), Name = "Keyboard",     Price = 79.99m,   CategoryId = ElectronicsId, IsActive = false, CreatedAt = DateTimeOffset.UtcNow.AddDays(-30), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30), Description = "Mechanical keyboard" },
            new Product { Id = Guid.NewGuid(), Name = "Mouse",        Price = 29.99m,   CategoryId = ElectronicsId, IsActive = true,  CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),  UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2),  Description = "Wireless mouse" },
            new Product { Id = Guid.NewGuid(), Name = "Dishwasher",   Price = 449.99m,  CategoryId = AppliancesId,  IsActive = true,  CreatedAt = DateTimeOffset.UtcNow.AddDays(-20), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-20), Description = "Energy-efficient dishwasher" },
            new Product { Id = Guid.NewGuid(), Name = "Microwave",    Price = 89.99m,   CategoryId = AppliancesId,  IsActive = true,  CreatedAt = DateTimeOffset.UtcNow.AddDays(-15), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-15), Description = "Countertop microwave" },
            new Product { Id = Guid.NewGuid(), Name = "Blender",      Price = 59.99m,   CategoryId = AppliancesId,  IsActive = false, CreatedAt = DateTimeOffset.UtcNow.AddDays(-8),  UpdatedAt = DateTimeOffset.UtcNow.AddDays(-8),  Description = "High-speed blender" },
            new Product { Id = Guid.NewGuid(), Name = "Coffee Maker", Price = 129.99m,  CategoryId = AppliancesId,  IsActive = true,  CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),  UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),  Description = "Programmable coffee maker" },
            new Product { Id = Guid.NewGuid(), Name = "Monitor",      Price = 299.99m,  CategoryId = ElectronicsId, IsActive = true,  CreatedAt = DateTimeOffset.UtcNow.AddDays(-7),  UpdatedAt = DateTimeOffset.UtcNow.AddDays(-7),  Description = "4K display" },
            new Product { Id = Guid.NewGuid(), Name = "Tablet",       Price = 399.99m,  CategoryId = ElectronicsId, IsActive = true,  CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),  UpdatedAt = DateTimeOffset.UtcNow.AddDays(-3),  Description = "10-inch tablet" });

        await db.SaveChangesAsync();
    }
}
