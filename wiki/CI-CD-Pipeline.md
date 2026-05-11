# CI/CD Pipeline

## Overview

The GitHub Actions pipeline is split into **three independent jobs** so failures are immediately identifiable:

```
push / PR
    │
    ├──► unit-tests         (fast — ~2 min, no Docker)
    │
    ├──► integration-tests  (medium — ~5 min, Docker via Testcontainers)
    │
    └──► e2e-tests          (depends on unit-tests passing — ~8 min)
         starts Postgres service container + API, then runs Playwright
```

---

## Job 1 — Unit Tests

```yaml
unit-tests:
  runs-on: ubuntu-latest
  steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        global-json-file: global.json   # pins SDK to 10.0.201

    - name: Restore
      run: dotnet restore NexaCommerce.slnx

    - name: Build
      run: dotnet build NexaCommerce.slnx --no-restore

    - name: Unit tests
      run: |
        dotnet test backend/product-catalog/NexaCommerce.ProductCatalog.Tests/... --no-build
        dotnet test backend/notifications/NexaCommerce.Notifications.Tests/...   --no-build
        dotnet test backend/report-scheduler/NexaCommerce.ReportScheduler.Tests/... --no-build
```

**Why project paths instead of the solution?**
Running `dotnet test NexaCommerce.slnx` would also attempt to run:
- `IntegrationTests.Common` — a shared library with no test runner (causes `MSB4181` error)
- `E2E.Tests` — Playwright tests that need a live server

Targeting projects explicitly avoids both problems.

---

## Job 2 — Integration Tests

```yaml
integration-tests:
  runs-on: ubuntu-latest
  steps:
    - uses: actions/checkout@v4
    - uses: actions/setup-dotnet@v4
      with: { global-json-file: global.json }
    - run: dotnet restore NexaCommerce.slnx
    - run: dotnet build NexaCommerce.slnx --no-restore
    - run: dotnet test tests/integration/NexaCommerce.ProductCatalog.IntegrationTests/... --no-build
```

**Testcontainers on GitHub Actions:**
`ubuntu-latest` runners include Docker. Testcontainers pulls `postgres:17-alpine` automatically — no separate service container needed. The container is started and stopped by the test fixture (`PostgreSqlFixture`), so no YAML services config is required.

---

## Job 3 — E2E Tests (Recommended Addition)

For E2E tests in CI you need a **running server**. The pattern is:

1. Start a Postgres **service container** (GitHub-native, faster than Testcontainers for this use case)
2. Start the ProductCatalog API as a **background process**
3. **Health-check poll** until `/health` returns 200
4. Install Playwright browsers
5. Run `dotnet test` with `BASE_URL` set

```yaml
e2e-tests:
  runs-on: ubuntu-latest
  needs: unit-tests

  services:
    postgres:
      image: postgres:17-alpine
      env:
        POSTGRES_DB: catalog
        POSTGRES_USER: nexacommerce
        POSTGRES_PASSWORD: nexacommerce
      ports: ["5432:5432"]
      options: --health-cmd pg_isready --health-interval 5s --health-retries 10

  steps:
    - uses: actions/checkout@v4
    - uses: actions/setup-dotnet@v4
      with: { global-json-file: global.json }

    - run: dotnet restore NexaCommerce.slnx
    - run: dotnet build NexaCommerce.slnx --no-restore

    - name: Start ProductCatalog API
      env:
        ConnectionStrings__catalog-db: "Host=localhost;Port=5432;Database=catalog;Username=nexacommerce;Password=nexacommerce"
        ASPNETCORE_URLS: "http://localhost:5001"
      run: dotnet run --project backend/product-catalog/NexaCommerce.ProductCatalog --no-build &

    - name: Wait for API healthy
      run: |
        for i in $(seq 1 30); do
          STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5001/health || echo "000")
          [ "$STATUS" = "200" ] && exit 0
          sleep 1
        done
        exit 1

    - name: Install Playwright
      run: pwsh tests/e2e/NexaCommerce.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium

    - name: E2E smoke tests
      env:
        BASE_URL: "http://localhost:5001"
      run: dotnet test tests/e2e/NexaCommerce.E2E.Tests --no-build --verbosity normal

    - name: Stop API
      if: always()
      run: pkill -f NexaCommerce.ProductCatalog || true
```

---

## Why Not `--locked-mode` for AppHost?

`Directory.Build.props` enables NuGet lock files (`RestorePackagesWithLockFile=true`). In CI you'd normally pass `--locked-mode` to enforce the committed lock file.

**The problem with AppHost:** The Aspire Dashboard SDK (`Aspire.Dashboard.Sdk`) is platform-specific. When you generate `packages.lock.json` on **Windows**, it records `Aspire.Dashboard.Sdk.win-x64`. On Linux CI, the restore needs `Aspire.Dashboard.Sdk.linux-x64` — which isn't in the Windows-generated lock file → restore fails.

**Fix options:**

| Option | Trade-off |
|---|---|
| Run `dotnet restore aspire/app-host --force-evaluate` on both platforms and commit the combined lock file | Lock file includes both RIDs — correct but requires regeneration on both OS |
| Add `<RestoreLockedMode>false</RestoreLockedMode>` to AppHost `.csproj` only | AppHost restore is unlocked; all other projects still locked |
| Don't build/test AppHost in CI (it's a dev-only project) | Simplest — AppHost is never deployed |

NexaCommerce uses option 3: the CI pipeline doesn't target the AppHost project directly.

---

## SDK Version Pinning

`global.json` pins the .NET SDK:
```json
{
  "sdk": { "version": "10.0.201", "rollForward": "latestFeature" }
}
```

In the workflow, `global-json-file: global.json` tells `setup-dotnet` to read the SDK version from this file. No hardcoding in YAML — upgrading the SDK is a single change in `global.json`.

---

## IntegrationTests.Common — Library, Not a Test Runner

`NexaCommerce.IntegrationTests.Common` is a **shared library** that contains `PostgreSqlFixture`. It uses `xunit.v3.extensibility.core` (not `xunit.v3`) so it compiles as a library without a test entry point.

`Directory.Build.props` sets `<IsTestProject>true</IsTestProject>` for everything under `tests/`. This causes `dotnet test` on the solution to try to run `IntegrationTests.Common` as a test executable — and fail with `MSB4181`.

**Fix:** Override in the project file:
```xml
<PropertyGroup>
  <IsTestProject>false</IsTestProject>
</PropertyGroup>
```
