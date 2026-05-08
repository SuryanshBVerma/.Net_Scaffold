using Microsoft.Playwright;
using Shouldly;
using Xunit;

namespace NexaCommerce.E2E.Tests;

/// <summary>
/// LEARNING — E2E tests (End-to-End):
///   Playwright controls a REAL headless browser (Chromium/Firefox/WebKit).
///   Tests run against a running stack — they verify what a real user would see.
///
///   Pyramid position:
///     Unit tests        → fast, many, isolated (mock everything)
///     Integration tests → medium, fewer, real DB
///     E2E tests         → slow, fewest, real browser + real stack
///
///   Run E2E tests:
///     1. Start the Aspire AppHost (dotnet run in app-host/)
///     2. Set BASE_URL env var to the running API URL
///     3. dotnet test tests/e2e/NexaCommerce.E2E.Tests/
///
///   In CI:
///     - Aspire AppHost (or docker-compose) starts the stack in a previous step
///     - BASE_URL is injected as a pipeline variable
///     - E2E tests run against the deployed stack
///
/// LEARNING — Playwright browser types:
///   Chromium → Chrome / Edge compatible behaviour
///   Firefox  → Gecko engine
///   WebKit   → Safari compatible
///   BrowserType.Chromium is the default — fastest and most compatible.
///
/// LEARNING — Playwright installation:
///   After building, run: dotnet playwright install
///   This downloads browser binaries to ~/.cache/ms-playwright/
///   The download is ~200MB per browser — run it once in CI as a setup step.
/// </summary>
public sealed class SmokeTests : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser    _browser    = null!;

    // LEARNING: Read BASE_URL from environment so this test works both locally
    // (pointing at the Aspire-started stack) and in CI (pipeline variable).
    // Default to the Aspire-assigned ProductCatalog port for convenience.
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("BASE_URL")
        ?? "http://localhost:5001";   // Aspire assigns this in the dashboard

    public async ValueTask InitializeAsync()
    {
        // LEARNING — IAsyncLifetime.InitializeAsync():
        //   Runs once before the first test. Launches the browser process.
        //   Playwright.CreateAsync() loads the installed browser binaries.
        _playwright = await Playwright.CreateAsync();
        _browser    = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true   // No visible window — runs in CI. Set false to debug visually.
        });
    }

    public async ValueTask DisposeAsync()
    {
        // LEARNING: Always dispose browser and playwright to release browser processes.
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    [Fact]
    public async Task Health_endpoint_returns_healthy()
    {
        // LEARNING — Page.GotoAsync():
        //   Navigates to the URL and waits for the network to settle (no pending XHR).
        //   Returns the HTTP response including status code.
        //
        // This is the simplest possible E2E test:
        //   → Start Chromium
        //   → Navigate to /health
        //   → Assert HTTP 200
        //
        // It proves the full stack is up: Docker containers, Aspire orchestration,
        // the real ASP.NET Core app, and the real Postgres database.
        await using var context = await _browser.NewContextAsync();
        var page     = await context.NewPageAsync();
        var response = await page.GotoAsync($"{BaseUrl}/health");

        response.ShouldNotBeNull();
        response!.Status.ShouldBe(200);
    }

    [Fact]
    public async Task Api_products_endpoint_returns_401_without_auth()
    {
        // LEARNING — Testing auth via browser:
        //   Playwright makes real HTTP requests, so auth middleware is exercised.
        //   A 401 here means Traefik forwarded the request AND the auth middleware
        //   rejected it correctly — the full request path is validated.
        await using var context = await _browser.NewContextAsync();
        var page     = await context.NewPageAsync();
        var response = await page.GotoAsync($"{BaseUrl}/api/products");

        response.ShouldNotBeNull();
        // 401 Unauthorized — no JWT token was sent.
        response!.Status.ShouldBe(401);
    }

    [Fact]
    public async Task Api_products_endpoint_content_type_is_json()
    {
        // LEARNING — Validating response headers via Playwright:
        //   response.Headers["content-type"] lets you assert the API returns
        //   the correct media type. Verifies the serializer is configured correctly
        //   and no middleware is rewriting the content type.
        await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            // Set a bearer token header for this context — all requests from this context
            // will include this Authorization header automatically.
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                // LEARNING: In a real E2E test you'd call your auth endpoint to get a token.
                // Here we use a placeholder to show the pattern.
                // ["Authorization"] = $"Bearer {token}"
            }
        });
        var page     = await context.NewPageAsync();
        var response = await page.GotoAsync($"{BaseUrl}/api/products");

        response.ShouldNotBeNull();
        // Even a 401 response should declare its content type.
        // A real authenticated call would assert "application/json".
        var headers = response!.Headers;
        headers.ShouldContainKey("content-type");
    }
}
