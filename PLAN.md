# MyStack — Enterprise .NET Scaffold Build Plan

## Domain: StoreFront Platform

A generic product management platform. Simple enough to understand immediately,
rich enough to demonstrate every architectural pattern.

**Services:**
- `ProductCatalog`  — CRUD web API for products + image uploads (ServiceA equivalent)
- `Notifications`   — Worker that reacts to product events via messaging (ServiceB equivalent)
- `ReportScheduler` — Quartz.NET cron jobs for cleanup and periodic reporting

---

## Project Naming Convention

| Layer | Name |
|---|---|
| Solution | `StoreFront.slnx` |
| Common library | `StoreFront.SharedKernel` |
| Aspire host | `StoreFront.AppHost` |
| Service web host | `StoreFront.ProductCatalog` |
| Service data layer | `StoreFront.ProductCatalog.Data` |
| Service unit tests | `StoreFront.ProductCatalog.Tests` |
| Worker host | `StoreFront.Notifications` |
| Scheduler host | `StoreFront.ReportScheduler` |
| Scheduler data | `StoreFront.ReportScheduler.Data` |
| Integration tests | `StoreFront.ProductCatalog.IntegrationTests` |
| Shared test fixtures | `StoreFront.IntegrationTests.Common` |
| E2E tests | `StoreFront.E2E.Tests` |

---

## Phases & Git Commits

---

### PHASE 1 — Repo Foundation
**Goal:** Every developer clones this and gets a working build immediately.
**Git commit:** `chore: initialise solution, central package management, and build configuration`

#### Files created:
```
StoreFront/
├── .gitignore
├── global.json                    ← Pin exact .NET SDK version
├── Directory.Build.props          ← Nullable, implicit usings, lock files — all projects
├── Directory.Packages.props       ← Every NuGet package version in one place (CPM)
├── NuGet.Config                   ← nuget.org feed, restore enabled
└── StoreFront.slnx                ← Solution file
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
backend/shared-kernel/StoreFront.SharedKernel/
├── StoreFront.SharedKernel.csproj
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
│   ├── ServiceCollectionExtensions.cs   ← AddStoreFrontDefaults(), AddWebDefaults(), AddFastEndpoints()
│   ├── ApplicationBuilderExtensions.cs  ← UseStoreFrontDefaults(), UseStoreFrontEndpoints(), MigrateDatabase<T>()
│   └── MessagingExtensions.cs           ← AddRabbitMQMessaging() — Wolverine + env-scoped queues + inbox/outbox
│
└── Storage/
    ├── IObjectStorageService.cs         ← Upload / Download / Delete abstraction
    └── MinioObjectStorageService.cs     ← S3-compatible impl (MinIO dev → AWS S3 prod, zero code change)
```

#### What to learn here:
- `IUserContext` — inject typed identity everywhere instead of reading raw `ClaimsPrincipal`
- `RequirePermissionAttribute` + `PermissionPreProcessor` — permission checks without policy boilerplate
- `AddStoreFrontDefaults()` — Serilog structured logging + OpenTelemetry traces/metrics + health checks in a single call
- `MigrateDatabase<T>()` — idempotent EF Core auto-migration on startup
- `MessagingExtensions` — Wolverine inbox/outbox + env-scoped RabbitMQ queue names
- `IObjectStorageService` — storage abstraction: MinIO locally, AWS S3 in production, no code change

---

### PHASE 3 — Aspire AppHost (All 3 Resource Modes)
**Goal:** Single `dotnet run` boots the entire stack. Demonstrates every Aspire capability.
**Git commit:** `feat(aspire): add AppHost showcasing all three Aspire resource modes`

#### Files created:
```
aspire/app-host/
├── StoreFront.AppHost.csproj
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
var catalog = builder.AddProject<Projects.StoreFront_ProductCatalog>("product-catalog")
    .WithReference(catalogDb)
    .WithReference(rabbitMq)
    .WaitFor(catalogDb);
```
→ Aspire compiles, hot-restarts, injects connection strings and OTEL endpoint automatically.

**Mode 3 — Local npm process** (non-.NET apps you own):
```csharp
var frontend = builder.AddNpmApp("frontend", "../../frontend/storefront-ui")
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
**Git commit:** `feat(product-catalog): add ProductCatalog web API with EF Core, FastEndpoints, Wolverine, and MinIO`

#### Files created:
```
backend/product-catalog/
├── StoreFront.ProductCatalog/          ← Web host
│   ├── StoreFront.ProductCatalog.csproj
│   ├── Program.cs                      ← ~20 line startup using SharedKernel extensions
│   ├── appsettings.json
│   ├── Dockerfile                      ← Multi-stage: SDK build → ASP.NET runtime
│   │
│   ├── Endpoints/
│   │   └── ProductEndpoints.cs         ← All product endpoints in REPR pattern
│   │       ├── GetProductEndpoint      ← GET /api/products/{id}
│   │       ├── ListProductsEndpoint    ← GET /api/products
│   │       ├── CreateProductEndpoint   ← POST /api/products  [RequirePermission("products:write")]
│   │       ├── UpdateProductEndpoint   ← PUT /api/products/{id}
│   │       ├── DeleteProductEndpoint   ← DELETE /api/products/{id}
│   │       └── UploadProductImageEndpoint ← POST /api/products/{id}/image → MinIO
│   │
│   ├── Services/
│   │   ├── IProductService.cs          ← Returns Result<T> — no exceptions for business failures
│   │   └── ProductService.cs           ← Business logic: EF Core + Wolverine publish + MinIO upload
│   │
│   └── Messaging/
│       ├── ProductCreatedEvent.cs      ← Published when a product is created
│       └── ProductDeletedEvent.cs      ← Published when a product is deleted
│
├── StoreFront.ProductCatalog.Data/     ← EF Core layer (owns its own DB schema)
│   ├── StoreFront.ProductCatalog.Data.csproj
│   ├── CatalogDbContext.cs
│   ├── Entities/
│   │   └── Product.cs                  ← Id, Name, Description, Price, ImageKey, CreatedAt
│   └── Migrations/                     ← EF Core generated migrations
│
└── StoreFront.ProductCatalog.Tests/    ← Unit tests
    ├── StoreFront.ProductCatalog.Tests.csproj
    └── Services/
        └── ProductServiceTests.cs      ← Moq + Shouldly + InMemory EF
```

#### What to learn here:
- `Program.cs` in ~20 lines using SharedKernel extensions
- **REPR Pattern** (Request → Endpoint → Response) — one class per HTTP operation
- **Ardalis.Result** — `Result<T>` return type; `SendResult()` auto-maps to correct HTTP status
- **Riok.Mapperly** — compile-time source-generated mapping; no reflection, errors at build
- **Wolverine publish** inside a DB transaction — inbox/outbox guarantees at-least-once delivery
- **MinIO upload** via `IObjectStorageService` — same code runs against AWS S3 in production
- `[RequirePermission("products:write")]` — enforced by pre-processor before handler runs

---

### PHASE 5 — Notifications Service (Event-Driven Worker)
**Goal:** Demonstrate async service-to-service communication with zero HTTP coupling.
**Git commit:** `feat(notifications): add Notifications worker consuming ProductCatalog events via RabbitMQ`

#### Files created:
```
backend/notifications/
├── StoreFront.Notifications/
│   ├── StoreFront.Notifications.csproj
│   ├── Program.cs                       ← Worker host (no HTTP) — messaging only
│   │
│   ├── Handlers/
│   │   ├── ProductCreatedHandler.cs     ← Wolverine discovers this by convention
│   │   └── ProductDeletedHandler.cs
│   │
│   └── Services/
│       ├── INotificationSender.cs
│       └── NotificationSender.cs        ← Stub: logs the notification (swap with email/webhook)
│
└── StoreFront.Notifications.Tests/
    └── Handlers/
        └── ProductCreatedHandlerTests.cs
```

#### What to learn here:
- **Worker host** (not a web host) — `Host.CreateApplicationBuilder()`, no HTTP pipeline
- **Wolverine handler discovery** — `Handle(ProductCreatedEvent)` is found by convention, no registration
- **Decoupled services** — Notifications knows nothing about ProductCatalog's internals; only the event contract is shared
- **Retry + dead-letter** — Wolverine retries failed handlers with exponential backoff automatically
- Why workers are **not routed through Traefik** — they have no HTTP surface

---

### PHASE 6 — Report Scheduler Service (Quartz.NET)
**Goal:** Demonstrate scheduled background jobs with persistent state and audit logging.
**Git commit:** `feat(report-scheduler): add ReportScheduler with Quartz.NET persistent store, cron jobs, and audit log`

#### Files created:
```
backend/report-scheduler/
├── StoreFront.ReportScheduler/
│   ├── StoreFront.ReportScheduler.csproj
│   ├── Program.cs                        ← Worker host + Quartz.NET setup
│   ├── appsettings.json                  ← Cron expressions here, NOT in C# code
│   │
│   ├── Jobs/
│   │   ├── StaleProductCleanupJob.cs     ← Deletes products older than N days + audits
│   │   └── DailyReportJob.cs            ← Publishes ScheduledReportRequestedEvent via Wolverine
│   │
│   └── Scheduling/
│       └── JobRegistration.cs            ← Reads cron from config, registers with Quartz
│
├── StoreFront.ReportScheduler.Data/
│   ├── SchedulerDbContext.cs
│   └── Entities/
│       └── JobRunLog.cs                  ← Id, JobName, StartedAt, FinishedAt, Succeeded, Details
│
└── StoreFront.ReportScheduler.Tests/
    └── Jobs/
        └── StaleProductCleanupJobTests.cs
```

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
**Goal:** Show how all services are exposed under a single entry point with middleware.
**Git commit:** `feat(infra): add Traefik reverse proxy with static config, dynamic routes, and middleware examples`

#### Files created:
```
Infrastructure/traefik/
├── config/
│   ├── traefik.yml                  ← Static: entry points, dashboard, file provider, log level
│   └── dynamic/
│       ├── product-catalog.yml      ← Route: /api/products/** → product-catalog:8080
│       └── middlewares.yml          ← Reusable middleware: rate-limit, strip-prefix, https-redirect
└── plugins/
    └── README.md                    ← Explains WASM plugin pattern for future custom middleware
```

#### What to learn here:
- **Static vs Dynamic config** — static needs restart; dynamic reloads live (file watcher)
- Route-per-service pattern — each service gets its own dynamic config file
- **Reusable middleware** defined once, referenced by multiple routers
- Why workers (Notifications, ReportScheduler) have **no Traefik route** — they're invisible to HTTP

---

### PHASE 8 — Testing (Three-Tier Strategy)
**Goal:** Show the complete testing pyramid: unit → integration → E2E.
**Git commit:** `test: add unit tests, Testcontainers integration tests, and Playwright E2E smoke tests`

#### Files created:
```
tests/
├── common/
│   └── StoreFront.IntegrationTests.Common/
│       └── Fixtures/
│           ├── PostgreSqlFixture.cs    ← IAsyncLifetime: real PG container via Testcontainers
│           └── MinioFixture.cs         ← IAsyncLifetime: real MinIO container
│
├── integration/
│   └── StoreFront.ProductCatalog.IntegrationTests/
│       ├── CatalogDbContextTests.cs    ← Tests against real PostgreSQL
│       └── ProductCatalogApiTests.cs   ← WebApplicationFactory + real DB
│
└── e2e/
    └── StoreFront.E2E.Tests/
        └── SmokeTests.cs               ← Playwright: frontend loads, /health returns 200
```

#### What to learn here:
- **Unit tests** — Moq for dependencies, `Shouldly` for assertions, InMemory EF for DB queries
- **Testcontainers** — `IClassFixture<PostgreSqlFixture>` starts a real Docker container per test class
- Why **not to mock EF Core** in integration tests — real Npgsql catches SQL translation bugs
- **WebApplicationFactory** — boots the real `Program.cs` in test mode
- **Playwright** — headless Chromium, reads `BASE_URL` env var so CI can point at real stack

---

### PHASE 9 — Containerisation
**Goal:** Show multi-stage Dockerfiles and the dev/prod config separation pattern.
**Git commit:** `feat(containers): add multi-stage Dockerfiles, docker-compose, and dev override`

#### Files created:
```
backend/product-catalog/StoreFront.ProductCatalog/Dockerfile   ← Multi-stage: SDK → runtime
backend/report-scheduler/StoreFront.ReportScheduler/Dockerfile
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
frontend/storefront-ui/
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
| Wolverine | Async messaging + inbox/outbox | MassTransit (heavier), raw RabbitMQ (no outbox) |
| Quartz.NET | Cron job scheduling | Hangfire (needs SQL Server), Azure Functions (cloud-only) |
| Riok.Mapperly | Compile-time object mapping | AutoMapper (reflection, runtime errors) |
| EF Core + Npgsql | ORM + PostgreSQL | Dapper (no migrations), SQL Server (not open-source) |
| MinIO | Object storage (S3-compatible) | Azurite (Azure-only API, no web UI) |
| Traefik | Reverse proxy | Nginx (no live config reload), YARP (in-process only) |
| Serilog | Structured logging | Microsoft.Extensions.Logging only (no enrichers) |
| OpenTelemetry | Traces + metrics | Proprietary SDK (vendor lock-in) |
| Testcontainers | Real infra in tests | Mocking EF Core (misses SQL bugs) |
| Playwright | E2E browser tests | Selenium (slower, harder API) |
| xUnit v3 + Shouldly | Unit testing | NUnit, MSTest |
