using Microsoft.EntityFrameworkCore;
using NexaCommerce.ProductCatalog.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace NexaCommerce.IntegrationTests.Common.Fixtures;

/// <summary>
/// LEARNING — IAsyncLifetime:
///   xunit.v3's equivalent of IClassFixture setup/teardown.
///   InitializeAsync() runs once before the first test in the class.
///   DisposeAsync() runs once after the last test — stops and removes the container.
///
/// LEARNING — Testcontainers:
///   Testcontainers pulls a real Docker image and starts a container in-process.
///   No manual Docker setup needed — the container lifecycle is tied to the test run.
///   Each test class that injects this fixture gets its OWN container → full isolation.
///
/// LEARNING — Why NOT InMemory for integration tests:
///   InMemory EF skips:
///     • SQL translation — GROUP BY + navigation joins silently fall back to client-side
///     • Constraints   — unique indexes, FK violations are never raised
///     • Migrations    — schema is never validated against your actual EF model
///   Real Postgres catches all three. Integration tests exist specifically to catch
///   bugs that unit tests with InMemory miss.
///
/// USAGE in a test class:
///   public class MyTests(PostgreSqlFixture fixture) : IClassFixture&lt;PostgreSqlFixture&gt;
///   {
///       [Fact]
///       public async Task Some_test()
///       {
///           await using var db = fixture.CreateDbContext();
///           // use real Postgres here
///       }
///   }
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    // LEARNING: PostgreSqlBuilder configures the container before it starts.
    // The builder pattern mirrors Docker run options:
    //   .WithImage()    → which Docker image/tag to use
    //   .WithDatabase() → creates this DB on startup
    //   .WithUsername() → sets the postgres user
    //   .WithPassword() → sets the password
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")   // Pin tag — same as Aspire AppHost
        .WithDatabase("catalog_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    /// <summary>Connection string to the running Postgres container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// Create a fresh DbContext connected to the test Postgres container.
    /// Caller is responsible for disposing (use 'await using').
    /// </summary>
    public CatalogDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new CatalogDbContext(options);
    }

    /// <summary>
    /// LEARNING — IAsyncLifetime.InitializeAsync():
    ///   Called once before any test in the class runs.
    ///   StartAsync() pulls the image (first run only — cached after) and starts the container.
    ///   EnsureCreated() applies the EF model to create the schema — no migrations needed for tests.
    ///
    ///   In production tests you'd call MigrateAsync() instead to validate real migrations.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // Apply schema using EF Core (equivalent to running all migrations).
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// LEARNING — IAsyncLifetime.DisposeAsync():
    ///   Stops and removes the Docker container after all tests finish.
    ///   The container (and its data) is completely gone — no cleanup needed.
    ///   Next test run starts fresh.
    /// </summary>
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
