# NexaCommerce — Enterprise .NET Scaffold Build Plan

> **Project name:** NexaCommerce  
> A fictitious but realistic e-commerce backend. The name is generic enough that every
> pattern demonstrated here applies to any domain: HR, logistics, finance, etc.

## Domain: Product Management Platform

A generic product management platform. Simple enough to understand immediately,
rich enough to demonstrate every architectural pattern.

**Services:**
- `ProductCatalog`  — CRUD web API for products + image uploads
- `Notifications`   — Worker that reacts to product events via messaging
- `ReportScheduler` — Quartz.NET cron jobs for cleanup and periodic reporting

---

## Architecture Decisions (Read Before Starting)

### ❓ Should all services be wired through Traefik?
**No.** Only HTTP-facing services get a Traefik route:
- ✅ `ProductCatalog` — exposes `GET/POST/PUT/DELETE /api/products/**` → gets a route
- ❌ `Notifications` — worker with no HTTP surface → no Traefik route (correct by design)
- ❌ `ReportScheduler` — worker with no HTTP surface → no Traefik route (correct by design)

Wiring workers through Traefik is a mistake beginners make. This scaffold makes it
explicit: workers are invisible to HTTP — they only communicate via the message bus.

### ❓ Can Wolverine replace RabbitMQ?
**No — they are different layers, not alternatives:**

| Layer | Technology | Analogy |
|---|---|---|
| Messaging framework | **Wolverine** | EF Core (the ORM) |
| Message transport | **RabbitMQ** (or others) | PostgreSQL (the database) |

Wolverine is the framework. It needs a transport to carry messages. Built-in options:
- `Wolverine` alone → **in-process/local transport** (no broker, messages live in memory)
- `WolverineFx.RabbitMQ` → RabbitMQ as the durable broker
- `WolverineFx.AzureServiceBus` → Azure Service Bus

**Learning progression in this scaffold:**
1. Phases 2–5: Wolverine with **local transport** (zero extra infrastructure, learn the pattern cleanly)
2. Phase 6+: Add `WolverineFx.RabbitMQ` — **one line change** to swap transport — proves the abstraction

---

## Project Naming Convention

| Layer | Name |
|---|---|
| Layer | Name |
|---|---|
| Solution | `NexaCommerce.slnx` |
| Common library | `NexaCommerce.SharedKernel` |
| Aspire host | `NexaCommerce.AppHost` |
| Service web host | `NexaCommerce.ProductCatalog` |
| Service data layer | `NexaCommerce.ProductCatalog.Data` |
| Service unit tests | `NexaCommerce.ProductCatalog.Tests` |
| Worker host | `NexaCommerce.Notifications` |
| Scheduler host | `NexaCommerce.ReportScheduler` |
| Scheduler data | `NexaCommerce.ReportScheduler.Data` |
| Integration tests | `NexaCommerce.ProductCatalog.IntegrationTests` |
| Shared test fixtures | `NexaCommerce.IntegrationTests.Common` |
| E2E tests | `NexaCommerce.E2E.Tests` |

---

## Phases & Git Commits

---

### PHASE 1 — Repo Foundation
**Goal:** Every developer clones this and gets a working build immediately.
**Git commit:** `chore: initialise solution, central package management, and build configuration`

#### Files created:
```
NexaCommerce/
├── .gitignore
├── global.json                    ← Pin exact .NET SDK version
├── Directory.Build.props          ← Nullable, implicit usings, lock files — all projects
├── Directory.Packages.props       ← Every NuGet package version in one place (CPM)
├── NuGet.Config                   ← nuget.org feed, restore enabled
└── NexaCommerce.slnx              ← Solution file
```

#### What to learn here:
- `Directory.Packages.props` with `ManagePackageVersionsCentrally=true`
- `Directory.Build.props` — repo-wide MSBuild settings applied to all projects automatically
- `global.json` — pin the SDK so every machine and CI agent uses the same version
- Lock files (`RestorePackagesWithLockFile=true`) — reproducible NuGet restores

---

### PHASE 2 — Shared Kernel Library
**Goal:** One library all services consume. Defines the conventions for the whole repo.
**Git commit:** `feat(shared-kernel): add SharedKernel with auth, logging, observability, messaging, and storage extensions`

#### Files created:
```
backend/shared-kernel/NexaCommerce.SharedKernel/
├── NexaCommerce.SharedKernel.csproj
├── GlobalUsings.cs
│
├── Auth/
│   ├── IUserContext.cs                  ← Typed scoped identity (userId, roles, permissions)
│   ├── UserContext.cs                   ← Internal impl populated by middleware
│   ├── UserContextMiddleware.cs         ← Reads JWT claims → populates IUserContext
│   └── RequirePermissionAttribute.cs   ← [RequirePermission("products:write")]
│
├── Endpoints/
│   └── PermissionPreProcessor.cs        ← FastEndpoints pre-processor enforcing [RequirePermission]
│
├── Middleware/
│   └── CorrelationIdMiddleware.cs       ← X-Correlation-Id on every request + log enrichment
│
├── Extensions/
│   ├── ServiceCollectionExtensions.cs   ← AddNexaCommerceDefaults(), AddWebDefaults(), AddFastEndpoints()
│   ├── ApplicationBuilderExtensions.cs  ← UseNexaCommerceDefaults(), UseNexaEndpoints(), MigrateDatabase<T>()
│   └── MessagingExtensions.cs           ← AddMessaging() — Wolverine local transport (Phase 2) → RabbitMQ (Phase 6)
│
└── Storage/
    ├── IObjectStorageService.cs         ← Upload / Download / Delete abstraction
    └── MinioObjectStorageService.cs     ← S3-compatible impl (MinIO dev → AWS S3 prod, zero code change)
```

#### What to learn here:
- `IUserContext` — inject typed identity everywhere instead of reading raw `ClaimsPrincipal`
- `RequirePermissionAttribute` + `PermissionPreProcessor` — permission checks without policy boilerplate
- `AddNexaCommerceDefaults()` — Serilog structured logging + OpenTelemetry traces/metrics + health checks in a single call
- `MigrateDatabase<T>()` — idempotent EF Core auto-migration on startup
- `MessagingExtensions` — Wolverine with **local transport first** (no broker), then swap to RabbitMQ transport
- `IObjectStorageService` — storage abstraction: MinIO locally, AWS S3 in production, no code change

---

### PHASE 3 — Aspire AppHost (All 3 Resource Modes)
**Goal:** Single `dotnet run` boots the entire stack. Demonstrates every Aspire capability.
**Git commit:** `feat(aspire): add AppHost showcasing all three Aspire resource modes`

#### Files created:
```
aspire/app-host/
├── NexaCommerce.AppHost.csproj
├── appsettings.json         ← image versions, port map
└── Program.cs               ← THE file to study — all 3 modes shown with comments
```

#### What to learn here (the 3 modes shown in Program.cs):

**Mode 1 — Pre-built Docker images** (infrastructure you don't own):
```csharp
var postgres  = builder.AddPostgres("postgres").WithImage("postgres", "17-alpine").WithPgAdmin();
var rabbitMq  = builder.AddRabbitMQ("rabbitmq").WithImage("rabbitmq","4-management-alpine").WithManagementPlugin();
var minio     = builder.AddContainer("minio", "minio/minio", "...").WithArgs("server", "/data")...;
var traefik   = builder.AddContainer("traefik", "traefik", "v3.3").WithBindMount(...)...;
```

**Mode 2 — Built live from .csproj source** (services you own):
```csharp
var catalog = builder.AddProject<Projects.NexaCommerce_ProductCatalog>("product-catalog")
    .WithReference(catalogDb)
    .WithReference(rabbitMq)
    .WaitFor(catalogDb);
```
→ Aspire compiles, hot-restarts, injects connection strings and OTEL endpoint automatically.

**Mode 3 — Local npm process** (non-.NET apps you own):
```csharp
var frontend = builder.AddNpmApp("frontend", "../../frontend/nexacommerce-ui")
    .WithNpmPackageInstallation()
    .WithEnvironment("CATALOG_API_URL", catalog.GetEndpoint("http"))
    .WithHttpEndpoint(port: 4200, env: "PORT");
```
→ Runs `npm install` + `npm run start`, streams logs to Aspire dashboard, injects API URLs.

**Aspire Dashboard at http://localhost:15888:**
- Health status of every resource
- Structured log streaming from all services
- Distributed traces across service boundaries
- Metrics in real time

---

### PHASE 4 — ProductCatalog Service (Full Web API)
**Goal:** Complete web service demonstrating every backend pattern.
**Git commit:** `feat(product-catalog): add ProductCatalog web API with EF Core, LINQ, FastEndpoints, Wolverine, and MinIO`

#### Files created:
```
backend/product-catalog/
├── NexaCommerce.ProductCatalog/          ← Web host
│   ├── NexaCommerce.ProductCatalog.csproj
│   ├── Program.cs                        ← ~20 line startup using SharedKernel extensions
│   ├── appsettings.json
│   ├── Dockerfile                        ← Multi-stage: SDK build → ASP.NET runtime
│   │
│   ├── Endpoints/
│   │   └── ProductEndpoints.cs           ← All product endpoints in REPR pattern
│   │       ├── GetProductEndpoint        ← GET /api/products/{id}
│   │       ├── ListProductsEndpoint      ← GET /api/products?category=&minPrice=&maxPrice=
│   │       ├── CreateProductEndpoint     ← POST /api/products  [RequirePermission("products:write")]
│   │       ├── UpdateProductEndpoint     ← PUT /api/products/{id}
│   │       ├── DeleteProductEndpoint     ← DELETE /api/products/{id}
│   │       └── UploadProductImageEndpoint ← POST /api/products/{id}/image → MinIO
│   │
│   ├── Services/
│   │   ├── IProductService.cs            ← Returns Result<T> — no exceptions for business failures
│   │   └── ProductService.cs             ← Business logic: LINQ queries + Wolverine publish + MinIO upload
│   │
│   └── Messaging/
│       ├── ProductCreatedEvent.cs        ← Published when a product is created
│       └── ProductDeletedEvent.cs        ← Published when a product is deleted
│
├── NexaCommerce.ProductCatalog.Data/     ← EF Core layer (owns its own DB schema)
│   ├── NexaCommerce.ProductCatalog.Data.csproj
│   ├── CatalogDbContext.cs
│   ├── Entities/
│   │   ├── Product.cs                    ← Id, Name, Description, Price, Category, ImageKey, CreatedAt
│   │   └── Category.cs                   ← Id, Name (gives us a join to practice with LINQ)
│   └── Migrations/                       ← EF Core generated migrations
│
└── NexaCommerce.ProductCatalog.Tests/    ← Unit tests
    ├── NexaCommerce.ProductCatalog.Tests.csproj
    └── Services/
        └── ProductServiceTests.cs        ← Moq + Shouldly + InMemory EF + LINQ assertion patterns
```

#### What to learn here:
- `Program.cs` in ~20 lines using SharedKernel extensions
- **REPR Pattern** (Request → Endpoint → Response) — one class per HTTP operation
- **Ardalis.Result** — `Result<T>` return type; `SendResult()` auto-maps to correct HTTP status
- **Riok.Mapperly** — compile-time source-generated mapping; no reflection, errors at build
- **Wolverine publish** inside a DB transaction — inbox/outbox guarantees at-least-once delivery
- **MinIO upload** via `IObjectStorageService` — same code runs against AWS S3 in production
- `[RequirePermission("products:write")]` — enforced by pre-processor before handler runs

#### LINQ Learning — demonstrated in `ProductService.cs`:
```csharp
// Filtering: WHERE clause
var cheapProducts = await db.Products
    .Where(p => p.Price < request.MaxPrice && p.Category.Name == request.Category)
    .ToListAsync(ct);

// Projection: SELECT specific columns (avoids loading full entity)
var summaries = await db.Products
    .Select(p => new ProductSummary(p.Id, p.Name, p.Price, p.Category.Name))
    .ToListAsync(ct);

// Join via navigation property (EF translates to SQL JOIN)
var withCategory = await db.Products
    .Include(p => p.Category)
    .Where(p => p.IsActive)
    .OrderBy(p => p.Name)
    .ToListAsync(ct);

// Aggregation: GROUP BY + COUNT
var countByCategory = await db.Products
    .GroupBy(p => p.Category.Name)
    .Select(g => new { Category = g.Key, Count = g.Count(), AvgPrice = g.Average(p => p.Price) })
    .ToListAsync(ct);

// Pagination: SKIP + TAKE (server-side paging)
var page = await db.Products
    .OrderByDescending(p => p.CreatedAt)
    .Skip((request.Page - 1) * request.PageSize)
    .Take(request.PageSize)
    .ToListAsync(ct);

// Any / All / Count
var hasExpensive = await db.Products.AnyAsync(p => p.Price > 1000, ct);
var totalCount   = await db.Products.CountAsync(p => p.IsActive, ct);
```

All queries use `.AsNoTracking()` for read-only paths (performance) and tracked queries for writes.

---

### PHASE 5 — Notifications Service (Event-Driven Worker)
**Goal:** Demonstrate async service-to-service communication with zero HTTP coupling.
**Git commit:** `feat(notifications): add Notifications worker consuming ProductCatalog events via Wolverine`

#### Files created:
```
backend/notifications/
├── NexaCommerce.Notifications/
│   ├── NexaCommerce.Notifications.csproj
│   ├── Program.cs                       ← Worker host (no HTTP) — Wolverine local transport
│   │
│   ├── Handlers/
│   │   ├── ProductCreatedHandler.cs     ← Wolverine discovers this by convention
│   │   └── ProductDeletedHandler.cs
│   │
│   └── Services/
│       ├── INotificationSender.cs
│       └── NotificationSender.cs        ← Stub: logs the notification (swap with email/webhook)
│
└── NexaCommerce.Notifications.Tests/
    └── Handlers/
        └── ProductCreatedHandlerTests.cs
```

#### What to learn here:
- **Worker host** (not a web host) — `Host.CreateApplicationBuilder()`, no HTTP pipeline
- **Wolverine handler discovery** — `Handle(ProductCreatedEvent)` is found by convention, no registration
- **Wolverine local transport** — messages flow in-process first; no broker required to learn the pattern
- **Decoupled services** — Notifications knows nothing about ProductCatalog's internals; only the event contract is shared
- **Retry + dead-letter** — Wolverine retries failed handlers with exponential backoff automatically
- Why workers are **not routed through Traefik** — they have no HTTP surface

> ⚡ **Transport swap moment:** In `MessagingExtensions.cs`, changing from local to RabbitMQ transport is:
> ```csharp
> // Phase 5 (local — no broker):
> opts.UseInMemoryTransport();
>
> // Phase 6 upgrade (durable — RabbitMQ broker):
> opts.UseRabbitMq(new Uri(rabbitMqUri)).AutoProvision();
> ```
> The handlers, events, and service code do not change at all.

---

### PHASE 6 — Report Scheduler + RabbitMQ Transport Upgrade
**Goal:** Demonstrate Quartz.NET scheduling AND upgrade Wolverine to use RabbitMQ as the durable transport.
**Git commit:** `feat(report-scheduler): add ReportScheduler with Quartz.NET, audit log, and upgrade Wolverine to RabbitMQ transport`

#### Files created:
```
backend/report-scheduler/
├── NexaCommerce.ReportScheduler/
│   ├── NexaCommerce.ReportScheduler.csproj
│   ├── Program.cs                        ← Worker host + Quartz.NET setup
│   ├── appsettings.json                  ← Cron expressions here, NOT in C# code
│   │
│   ├── Jobs/
│   │   ├── StaleProductCleanupJob.cs     ← LINQ: bulk delete products older than N days
│   │   └── DailyReportJob.cs             ← Publishes ScheduledReportRequestedEvent via Wolverine
│   │
│   └── Scheduling/
│       └── JobRegistration.cs            ← Reads cron from config, registers with Quartz
│
├── NexaCommerce.ReportScheduler.Data/
│   ├── SchedulerDbContext.cs
│   └── Entities/
│       └── JobRunLog.cs                  ← Id, JobName, StartedAt, FinishedAt, Succeeded, Details
│
└── NexaCommerce.ReportScheduler.Tests/
    └── Jobs/
        └── StaleProductCleanupJobTests.cs
```

#### LINQ Learning — demonstrated in `StaleProductCleanupJob.cs`:
```csharp
// Bulk delete with LINQ (EF Core ExecuteDeleteAsync — no entity loading)
var cutoff = DateTimeOffset.UtcNow.AddDays(-thresholdDays);
var deleted = await db.JobRunLogs
    .Where(l => l.StartedAt < cutoff && l.Succeeded)
    .ExecuteDeleteAsync(ct);

// Bulk update with LINQ (EF Core ExecuteUpdateAsync)
var flagged = await db.Products
    .Where(p => p.CreatedAt < cutoff && !p.IsActive)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsArchived, true), ct);
```
Note: `ExecuteDeleteAsync` / `ExecuteUpdateAsync` translate to a single SQL statement — no loading into memory.

#### What to learn here:
- `IJob` interface with full DI — constructor injection works out of the box
- **Persistent job store** (`UsePostgres`) — job state survives service restarts
- `[DisallowConcurrentExecution]` — Quartz attribute preventing overlapping runs
- Cron externalized to `appsettings.json` — change schedule without recompiling
- **`IDbContextFactory<T>`** — correct way to use EF Core inside background/job services
- Jobs publishing to **RabbitMQ** (Wolverine from a worker host, not a web host)
- `JobRunLog` audit table — every job run recorded with outcome

---

### PHASE 7 — Infrastructure (Traefik Reverse Proxy)
**Goal:** Show how HTTP services are exposed under a single entry point, and why workers are NOT routed.
**Git commit:** `feat(infra): add Traefik reverse proxy with static config, dynamic routes, and middleware examples`

#### Files created:
```
Infrastructure/traefik/
├── config/
│   ├── traefik.yml                      ← Static: entry points, dashboard, file provider, log level
│   └── dynamic/
│       ├── product-catalog.yml          ← ✅ Route: /api/products/** → product-catalog:8080
│       ├── middlewares.yml              ← Reusable: rate-limit, strip-prefix, https-redirect
│       └── workers-have-no-routes.yml   ← 📝 Commented file explaining WHY workers are excluded
└── plugins/
    └── README.md                        ← WASM plugin pattern for future custom middleware
```

#### What to learn here:
- **Static vs Dynamic config** — static needs restart; dynamic reloads live (file watcher)
- **Route-per-service** — each HTTP service gets its own dynamic config file; add a service = add a file
- **Reusable middleware** — defined once in `middlewares.yml`, referenced by name in any router
- **Workers have no route** — `Notifications` and `ReportScheduler` are invisible to Traefik by design

#### Traefik routing table for this scaffold:
| Service | Has Traefik Route? | Why |
|---|---|---|
| `ProductCatalog` | ✅ Yes | Exposes HTTP API consumed by frontend and external clients |
| `Notifications` | ❌ No | Worker — only receives messages from RabbitMQ, no HTTP surface |
| `ReportScheduler` | ❌ No | Worker — only sends messages and runs cron jobs, no HTTP surface |

---

### PHASE 8 — Testing (Three-Tier Strategy)
**Goal:** Show the complete testing pyramid: unit → integration → E2E.
**Git commit:** `test: add unit tests, Testcontainers integration tests, and Playwright E2E smoke tests`

#### Files created:
```
tests/
├── common/
│   └── NexaCommerce.IntegrationTests.Common/
│       └── Fixtures/
│           ├── PostgreSqlFixture.cs    ← IAsyncLifetime: real PG container via Testcontainers
│           └── MinioFixture.cs         ← IAsyncLifetime: real MinIO container
│
├── integration/
│   └── NexaCommerce.ProductCatalog.IntegrationTests/
│       ├── CatalogDbContextTests.cs    ← LINQ queries tested against real PostgreSQL
│       └── ProductCatalogApiTests.cs   ← WebApplicationFactory + real DB
│
└── e2e/
    └── NexaCommerce.E2E.Tests/
        └── SmokeTests.cs               ← Playwright: frontend loads, /health returns 200
```

#### What to learn here:
- **Unit tests** — Moq for dependencies, `Shouldly` for assertions, InMemory EF for DB queries
- **Testcontainers** — `IClassFixture<PostgreSqlFixture>` starts a real Docker container per test class
- Why **not to mock EF Core** in integration tests — real Npgsql catches SQL translation bugs that InMemory misses
- **LINQ in tests** — asserting query results: `.ShouldContain()`, `.ShouldAllBe()`, `.Count().ShouldBe()`
- **WebApplicationFactory** — boots the real `Program.cs` in test mode
- **Playwright** — headless Chromium, reads `BASE_URL` env var so CI can point at real stack

#### LINQ in tests — what `CatalogDbContextTests.cs` covers:
```csharp
// Test: filtering works correctly
var results = await db.Products.Where(p => p.Price < 50).ToListAsync();
results.ShouldAllBe(p => p.Price < 50);

// Test: ordering is applied
var ordered = await db.Products.OrderBy(p => p.Name).ToListAsync();
ordered.Select(p => p.Name).ShouldBe(ordered.Select(p => p.Name).OrderBy(x => x));

// Test: pagination returns correct slice
var page2 = await db.Products.OrderBy(p => p.Id).Skip(10).Take(5).ToListAsync();
page2.Count.ShouldBe(5);

// Test: GroupBy aggregation
var groups = await db.Products.GroupBy(p => p.Category.Name)
    .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
groups.ShouldContain(g => g.Key == "Electronics" && g.Count > 0);
```

---

### PHASE 9 — Containerisation
**Goal:** Show multi-stage Dockerfiles and the dev/prod config separation pattern.
**Git commit:** `feat(containers): add multi-stage Dockerfiles, docker-compose, and dev override`

#### Files created:
```
backend/product-catalog/NexaCommerce.ProductCatalog/Dockerfile   ← Multi-stage: SDK → runtime
backend/report-scheduler/NexaCommerce.ReportScheduler/Dockerfile
docker-compose.yml                ← Service definitions (what runs)
docker-compose.override.yml       ← Dev env vars (how it's configured locally)
```

#### What to learn here:
- **Multi-stage Dockerfile** — build stage (~700MB SDK) + runtime stage (~200MB ASP.NET runtime)
- Copy `.csproj` files → restore → copy source → build (layer cache optimisation)
- `BUILD_CONFIGURATION` build arg — one Dockerfile handles both Debug (Aspire) and Release (CI)
- `docker-compose.override.yml` — dev env vars never touch the main compose file

---

### PHASE 10 — CI/CD Pipelines
**Goal:** Reusable pipeline templates. Adding a new service = 5 lines.
**Git commit:** `feat(pipelines): add reusable Azure DevOps pipeline templates for backend and testing`

#### Files created:
```
pipelines/
├── Templates/
│   ├── template-backend-build-push.yml   ← Reusable: restore → build → test → docker build → push
│   └── template-frontend-build-push.yml  ← Reusable: npm ci → build → docker push
├── Services/Build/
│   ├── product-catalog-build-push.yml    ← 8 lines calling the template
│   ├── notifications-build-push.yml
│   └── report-scheduler-build-push.yml
└── Testing/
    ├── integration-tests.yml             ← Testcontainers (requires Docker on agent)
    └── e2e-smoke.yml                     ← Boots Aspire stack → Playwright tests
```

#### What to learn here:
- **Template inheritance** (`extends:`) — all logic in one template; services pass parameters
- `--locked-mode` restore — CI fails if lock file is out of sync (prevents dependency drift)
- Testcontainers in CI — Ubuntu agents have Docker pre-installed; just run `dotnet test`
- E2E pipeline pattern — boot stack in background → health poll → run tests → kill stack

---

### PHASE 11 — Frontend Placeholder
**Goal:** Show Aspire Mode 3 has something to connect to.
**Git commit:** `feat(frontend): add Angular placeholder configured for Aspire npm integration`

#### Files created:
```
frontend/nexacommerce-ui/
├── package.json       ← scripts: start, build, test, lint
└── README.md          ← How Aspire injects CATALOG_API_URL and how Angular reads it
```

---

## Summary Table

| Phase | Commit message | Key learning |
|---|---|---|
| 1 | `chore: initialise solution...` | CPM, lock files, global.json |
| 2 | `feat(shared-kernel): add SharedKernel...` | Cross-cutting concerns as opt-in extensions |
| 3 | `feat(aspire): add AppHost...` | All 3 Aspire resource modes |
| 4 | `feat(product-catalog): add ProductCatalog...` | REPR, Result, Mapperly, Wolverine, MinIO |
| 5 | `feat(notifications): add Notifications worker...` | Decoupled async messaging, worker host |
| 6 | `feat(report-scheduler): add ReportScheduler...` | Quartz.NET, persistent store, audit log |
| 7 | `feat(infra): add Traefik reverse proxy...` | Static/dynamic config, route-per-service |
| 8 | `test: add unit, integration, E2E tests` | Testcontainers, WebApplicationFactory, Playwright |
| 9 | `feat(containers): add Dockerfiles, docker-compose...` | Multi-stage builds, config separation |
| 10 | `feat(pipelines): add Azure DevOps templates...` | Reusable templates, locked restore, CI E2E |
| 11 | `feat(frontend): add Angular placeholder...` | Aspire Mode 3 — npm process integration |

---

## Realistic Technology Map

| Technology | Purpose | Alternative considered |
|---|---|---|
| .NET 10 | Runtime | — |
| Aspire 9 | Local dev orchestration | docker-compose only (worse DX) |
| FastEndpoints | HTTP API (REPR pattern) | ASP.NET Controllers (too much boilerplate) |
| Ardalis.Result | Typed success/failure | Exceptions for flow control (anti-pattern) |
| **LINQ** | Data querying (filter, project, group, page) | Raw SQL strings (not type-safe) |
| Wolverine | Messaging framework (local → RabbitMQ transport) | MassTransit (heavier), raw RabbitMQ (no outbox) |
| RabbitMQ | Message broker (Wolverine's durable transport) | Azure Service Bus (cloud-only), Kafka (overkill) |
| Quartz.NET | Cron job scheduling | Hangfire (needs SQL Server), Azure Functions (cloud-only) |
| Riok.Mapperly | Compile-time object mapping | AutoMapper (reflection, runtime errors) |
| EF Core + Npgsql | ORM + PostgreSQL | Dapper (no migrations), SQL Server (not open-source) |
| MinIO | Object storage (S3-compatible) | Azurite (Azure-only API, no web UI) |
| Traefik | Reverse proxy (HTTP services only) | Nginx (no live config reload), YARP (in-process only) |
| Serilog | Structured logging | Microsoft.Extensions.Logging only (no enrichers) |
| OpenTelemetry | Traces + metrics | Proprietary SDK (vendor lock-in) |
| Testcontainers | Real infra in tests | Mocking EF Core (misses SQL bugs) |
| Playwright | E2E browser tests | Selenium (slower, harder API) |
| xUnit v3 + Shouldly | Unit testing | NUnit, MSTest |
