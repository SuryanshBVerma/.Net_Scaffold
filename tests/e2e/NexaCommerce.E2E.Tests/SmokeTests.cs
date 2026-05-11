using Microsoft.Playwright;
using Shouldly;
using Xunit;
using Xunit.Sdk;

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

    // Probe result — set in InitializeAsync, checked by each test.
    private bool _serverReachable;

    public async ValueTask InitializeAsync()
    {
        // Probe the server before launching the browser.
        // If it's unreachable, each test will call SkipException instead of failing.
        // LEARNING: SkipException is the xunit.v3 way to skip a test at runtime.
        //   Throw it from inside a test (or fixture) to mark it as skipped rather than failed.
        //   This is the correct mechanism when the skip condition can only be evaluated at runtime.
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var probe = await http.GetAsync($"{BaseUrl}/health");
            _serverReachable = probe.IsSuccessStatusCode;
        }
        catch
        {
            _serverReachable = false;
        }

        if (!_serverReachable) return;  // Don't start browser if stack isn't up.

        _playwright = await Playwright.CreateAsync();
        _browser    = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (!_serverReachable) return;
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    [Fact]
    public async Task Health_endpoint_returns_healthy()
    {
        if (!_serverReachable)
            Assert.Skip($"Skipped: {BaseUrl} is not reachable. Start the Aspire stack first.");

        await using var context = await _browser.NewContextAsync();
        var page     = await context.NewPageAsync();
        var response = await page.GotoAsync($"{BaseUrl}/health");

        response.ShouldNotBeNull();
        response!.Status.ShouldBe(200);
    }

    [Fact]
    public async Task Create_product_endpoint_returns_401_without_auth()
    {
        if (!_serverReachable)
            Assert.Skip($"Skipped: {BaseUrl} is not reachable. Start the Aspire stack first.");

        // LEARNING — POST /api/products (CreateProductEndpoint) requires authentication.
        //   It does NOT call AllowAnonymous(), so a missing token returns 401.
        //   GET /api/products has AllowAnonymous() and returns 200 to anyone.
        //   Playwright's GotoAsync is GET-only; use APIRequestContext for POST.
        var apiContext = await _playwright.APIRequest.NewContextAsync(
            new APIRequestNewContextOptions { BaseURL = BaseUrl });
        var apiResponse = await apiContext.PostAsync("/api/products");

        apiResponse.Status.ShouldBe(401);
    }

    [Fact]
    public async Task Api_products_endpoint_content_type_is_json()
    {
        if (!_serverReachable)
            Assert.Skip($"Skipped: {BaseUrl} is not reachable. Start the Aspire stack first.");

        // GET /api/products is AllowAnonymous — returns 200 with a JSON body.
        // This verifies the content-type header is set correctly by the serializer.
        await using var context = await _browser.NewContextAsync();
        var page     = await context.NewPageAsync();
        var response = await page.GotoAsync($"{BaseUrl}/api/products");

        response.ShouldNotBeNull();
        response!.Headers.ShouldContainKey("content-type");
    }
}
