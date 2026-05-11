# Testing Strategy

## The Testing Pyramid

```
        /‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾\
       /   E2E Tests (3)  \        Playwright · real browser · real HTTP
      /‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾\
     /  Integration (16)   \      Testcontainers · real PostgreSQL
    /‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾\
   /    Unit Tests (24)      \    InMemory EF · mocked dependencies · fast
  /‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾\
```

| Layer | Count | Speed | Infrastructure |
|---|---|---|---|
| Unit | 24 | ~2s | None |
| Integration | 16 | ~10s | Docker (Testcontainers) |
| E2E | 3 | Skip when no server | Aspire stack or CI job |

---

## Unit Tests

**Projects:** `ProductCatalog.Tests`, `Notifications.Tests`, `ReportScheduler.Tests`

Unit tests mock all I/O and test business logic in isolation. xUnit v3 + Moq:

```csharp
public class ProductServiceTests
{
    private readonly Mock<IMessageBus>            _bus     = new();
    private readonly Mock<IObjectStorageService>  _storage = new();
    private readonly CatalogDbContext             _db;

    public ProductServiceTests()
    {
        // InMemory EF — fast, no Docker, but skips SQL constraints
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())  // unique DB per test
            .Options;
        _db = new CatalogDbContext(options);
    }

    [Fact]
    public async Task CreateAsync_should_create_product_and_publish_event()
    {
        // Arrange — seed a category
        _db.Categories.Add(new Category { Id = _categoryId, Name = "Electronics" });
        await _db.SaveChangesAsync();

        var sut = new ProductService(_db, _bus.Object, _storage.Object, ...);

        // Act
        var result = await sut.CreateAsync(
            new CreateProductRequest("Laptop", null, 999.99m, _categoryId), default);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _db.Products.Should().HaveCount(1);
        _bus.Verify(b => b.PublishAsync(It.IsAny<ProductCreated>(), default), Times.Once);
    }
}
```

**Why InMemory for unit tests?**
- No Docker — runs anywhere, including developer machines without Docker Desktop
- Sub-millisecond per test
- Forces clean separation of business logic from SQL concerns

**What InMemory misses (why you also need integration tests):**
- `GROUP BY` runs client-side — never hits SQL
- Unique index violations are not enforced
- FK constraints are not enforced
- Migrations are never validated

---

## Integration Tests — Testcontainers

**Project:** `ProductCatalog.IntegrationTests`

Testcontainers starts a real Docker container inside the test process. The container lifecycle is tied to the test run — no manual setup needed.

```csharp
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("catalog_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public CatalogDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new CatalogDbContext(options);
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();   // creates schema
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
```

### Fixture Seeding — Why One Fixture

xUnit v3 runs tests **in parallel** on multi-core machines. The original pattern had each test call `SeedAsync()`. On a 4-core GitHub Actions runner:

1. Test A checks "no data" → sees empty DB
2. Test B checks "no data" → also sees empty DB
3. Test A inserts → commits category `id=a0000...001`
4. Test B inserts → PK violation on `id=a0000...001`

**Fix:** Move seeding to `IAsyncLifetime.InitializeAsync()` — xUnit v3 guarantees this runs **once, sequentially, before any test**:

```csharp
public sealed class CatalogDbFixture : IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgres = new();

    public static readonly Guid ElectronicsId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    public static readonly Guid AppliancesId  = Guid.Parse("a0000000-0000-0000-0000-000000000002");

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        await SeedAsync();   // runs once — no parallelism yet
    }

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();
        // Clear HasData seeds first (EnsureCreatedAsync inserts them)
        await db.Products.ExecuteDeleteAsync();
        await db.Categories.ExecuteDeleteAsync();
        // Insert controlled test dataset
        db.Categories.AddRange(...);
        db.Products.AddRange(...);
        await db.SaveChangesAsync();
    }
}
```

### WebApplicationFactory — API Tests Without a Real Server

`ProductCatalogApiTests` boots the full `Program.cs` in-memory using `WebApplicationFactory<Program>`. No TCP port, no Docker — the HTTP client talks to the test server in-process:

```csharp
public sealed class ProductCatalogApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ProductCatalogApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(host =>
            {
                host.ConfigureServices(services =>
                {
                    // Remove Npgsql pool (registered by AddNpgsqlDbContext)
                    // Replace with InMemory — no Postgres needed for these smoke tests
                    var toRemove = services
                        .Where(d => d.ServiceType.IsGenericType &&
                                    d.ServiceType.GetGenericArguments()
                                     .Any(t => t == typeof(CatalogDbContext)))
                        .ToList();
                    foreach (var d in toRemove) services.Remove(d);

                    services.AddDbContextPool<CatalogDbContext>(options =>
                        options.UseInMemoryDatabase("catalog-test"));
                });
            })
            .CreateClient();
    }

    [Fact]
    public async Task Health_endpoint_returns_200()
    {
        var response = await _client.GetAsync("/health");
        response.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task Create_product_endpoint_returns_401_without_auth()
    {
        // POST /api/products has no AllowAnonymous() → 401 without JWT
        var response = await _client.PostAsync("/api/products", null);
        ((int)response.StatusCode).ShouldBe(401);
    }
}
```

**Key gotcha — `AddDbContextPool` not `AddDbContext`:**
`AddNpgsqlDbContext` (Aspire) registers a pooled context which also creates `IDbContextPool<T>` and `IScopedDbContextLease<T>`. Replacing only `DbContextOptions<T>` leaves those singletons broken. You must remove all generic registrations involving `CatalogDbContext` and re-register with `AddDbContextPool`.

---

## E2E Tests — Playwright

**Project:** `NexaCommerce.E2E.Tests`

Playwright controls a real headless Chromium browser. Tests require a running server.

```csharp
public sealed class SmokeTests : IAsyncLifetime
{
    private bool _serverReachable;

    public async ValueTask InitializeAsync()
    {
        // Probe first — skip gracefully if no stack is running
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var probe = await http.GetAsync($"{BaseUrl}/health");
            _serverReachable = probe.IsSuccessStatusCode;
        }
        catch { _serverReachable = false; }

        if (!_serverReachable) return;

        _playwright = await Playwright.CreateAsync();
        _browser    = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    [Fact]
    public async Task Health_endpoint_returns_healthy()
    {
        // Assert.Skip() is xUnit v3's runtime skip mechanism
        if (!_serverReachable)
            Assert.Skip($"Skipped: {BaseUrl} is not reachable. Start the Aspire stack first.");

        await using var context = await _browser.NewContextAsync();
        var page     = await context.NewPageAsync();
        var response = await page.GotoAsync($"{BaseUrl}/health");
        response!.Status.ShouldBe(200);
    }
}
```

### Running E2E Tests Locally

```bash
# 1. Start the full Aspire stack
dotnet run --project aspire/app-host

# 2. Find the ProductCatalog URL in the Aspire dashboard (e.g., http://localhost:5001)

# 3. Run E2E tests in a separate terminal
$env:BASE_URL = "http://localhost:5001"
dotnet test tests/e2e/NexaCommerce.E2E.Tests
```

### First-time Setup

Playwright requires browser binaries (not included in the NuGet package):
```powershell
# After building the project
powershell tests/e2e/NexaCommerce.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

---

## Common Testing Pitfalls

| Problem | Cause | Fix |
|---|---|---|
| PK violation in parallel tests | Multiple tests calling `SeedAsync()` simultaneously | Seed in `IAsyncLifetime.InitializeAsync()` — runs before parallelism |
| GROUP BY test passes locally but fails in CI | InMemory executes GROUP BY client-side | Test against real Postgres via Testcontainers |
| `IScopedDbContextLease` not registered | Only removed `DbContextOptions` from DI | Remove all generic registrations, re-add with `AddDbContextPool` |
| `MigrateAsync()` throws on InMemory | InMemory has no migration concept | Check `ProviderName` — call `EnsureCreatedAsync()` for InMemory |
| E2E tests fail in `dotnet test` without server | No running server | `Assert.Skip()` in `InitializeAsync()` when server unreachable |
| `SkipException(string)` doesn't compile | Constructor doesn't take a string in xunit.v3 | Use `Assert.Skip(reason)` instead |
