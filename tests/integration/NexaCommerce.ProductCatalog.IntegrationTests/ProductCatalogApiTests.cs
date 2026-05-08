using Microsoft.AspNetCore.Mvc.Testing;
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
            //   Here we configure an InMemory database so the test server
            //   starts without needing a real Postgres connection.
            .WithWebHostBuilder(host =>
            {
                host.UseSetting("ConnectionStrings:catalog-db",
                    "Host=localhost;Database=catalog_test;");
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
    public async Task Products_endpoint_returns_401_without_auth()
    {
        // LEARNING: The /api/products endpoint requires JWT authentication.
        // An unauthenticated request should return 401 Unauthorized.
        // This verifies the auth middleware is wired correctly — not skipped.
        var response = await _client.GetAsync("/api/products");
        ((int)response.StatusCode).ShouldBe(401);
    }
}
