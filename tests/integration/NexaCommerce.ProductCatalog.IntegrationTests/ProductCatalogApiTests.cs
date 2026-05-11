using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexaCommerce.ProductCatalog.Data;
using Shouldly;
using Xunit;

namespace NexaCommerce.ProductCatalog.IntegrationTests;

/// <summary>
/// LEARNING — WebApplicationFactory:
///   Boots the REAL Program.cs of NexaCommerce.ProductCatalog inside the test process.
///   No Docker, no network — the full ASP.NET Core pipeline runs in-memory.
///   The HTTP client talks to the real endpoints with the real middleware stack.
///
///   Use this for:
///     • Verifying the full request pipeline (auth, routing, serialization)
///     • Testing that endpoints exist and return the right status codes
///     • Smoke-testing the app compiles and starts without errors
///
///   NOT a replacement for database integration tests — you still need Testcontainers
///   if you want to test LINQ queries against real Postgres.
///
/// LEARNING — Test server vs real server:
///   WebApplicationFactory creates an in-memory test server (no real TCP port).
///   CreateClient() returns an HttpClient pre-wired to that test server.
///   No port binding, no firewall, no flakiness from port conflicts.
///
/// LEARNING — Environment:
///   The test server uses "Development" environment by default.
///   Aspire connection strings are not injected — configure test-specific
///   appsettings via WithWebHostBuilder() to override.
/// </summary>
public sealed class ProductCatalogApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ProductCatalogApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            // LEARNING — WithWebHostBuilder:
            //   Override configuration for the test environment.
            //   Program.cs calls builder.AddNpgsqlDbContext<CatalogDbContext>("catalog-db")
            //   which registers both the DbContext AND a Postgres health check.
            //   In CI there is no Postgres, so we replace the DbContext registration
            //   with an InMemory provider and remove the Postgres health check so
            //   the WebApplicationFactory boots without a live database.
            .WithWebHostBuilder(host =>
            {
                host.ConfigureServices(services =>
                {
                    // AddNpgsqlDbContext (Aspire) registers a pooled DbContext plus
                    // IDbContextPool<T>, IScopedDbContextLease<T>, and DbContextOptions<T>.
                    // Removing only DbContextOptions leaves the pool singleton referencing
                    // a missing scoped options → lifetime conflict exception.
                    // Remove every registration that mentions CatalogDbContext as a
                    // generic argument, then re-register a plain (non-pooled) InMemory context.
                    var toRemove = services
                        .Where(d => d.ServiceType.IsGenericType &&
                                    d.ServiceType.GetGenericArguments()
                                     .Any(t => t == typeof(CatalogDbContext)))
                        .ToList();
                    foreach (var d in toRemove) services.Remove(d);

                    // Re-register with InMemory using DbContextPool (not AddDbContext) to
                    // preserve IScopedDbContextLease<T> — which the Aspire EF health check
                    // resolves internally. Without pooling that type is never registered and
                    // the health check throws "No service for IScopedDbContextLease".
                    services.AddDbContextPool<CatalogDbContext>(options =>
                        options.UseInMemoryDatabase("catalog-test"));
                });
            })
            .CreateClient();
    }

    [Fact]
    public async Task Health_endpoint_returns_200()
    {
        // LEARNING: The /health endpoint is registered by AddNexaCommerceDefaults()
        // via services.AddHealthChecks() + endpoints.MapHealthChecks("/health").
        // This test verifies the app starts and the health endpoint responds.
        // In CI, this is the first gate — if the app can't start, all other tests are moot.
        var response = await _client.GetAsync("/health");
        response.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task Create_product_endpoint_returns_401_without_auth()
    {
        // LEARNING: POST /api/products (CreateProductEndpoint) does NOT call
        // AllowAnonymous(), so FastEndpoints enforces authentication by default.
        // An unauthenticated request must return 401 Unauthorized.
        // This verifies the auth middleware is wired correctly — not skipped.
        var response = await _client.PostAsync("/api/products", null);
        ((int)response.StatusCode).ShouldBe(401);
    }
}
