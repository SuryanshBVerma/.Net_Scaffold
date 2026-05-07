// ═══════════════════════════════════════════════════════════════════════════
// NexaCommerce.AppHost — Program.cs
// ═══════════════════════════════════════════════════════════════════════════
//
// THE FILE TO STUDY IN PHASE 3.
//
// This file is the Aspire orchestration manifest. It declares:
//   - What services/infrastructure to run
//   - How they connect to each other (references, env vars)
//   - Startup order (WaitFor)
//   - Port assignments
//
// THREE ASPIRE RESOURCE MODES demonstrated here:
//
//   MODE 1 — Pre-built Docker images    [ACTIVE now]
//     Infrastructure you don't own (Postgres, MinIO, RabbitMQ, Traefik).
//     Aspire pulls the image and runs the container.
//     You configure it; you don't build it.
//
//   MODE 2 — .NET projects built from source    [UNCOMMENT in Phases 4/5/6]
//     Services you own (ProductCatalog, Notifications, ReportScheduler).
//     Aspire compiles them, runs them, hot-restarts on change.
//     Connection strings and OTEL endpoint injected automatically.
//
//   MODE 3 — Local npm process    [UNCOMMENT in Phase 11]
//     Non-.NET apps you own (Angular frontend).
//     Aspire runs `npm install` + `npm run start`, streams logs to dashboard.
//     API URLs injected as environment variables automatically.
//
// ASPIRE DASHBOARD at http://localhost:15888 (started automatically):
//   • Health status of every resource
//   • Structured log streaming from all services
//   • Distributed traces across service boundaries (click a trace → full call chain)
//   • Metrics in real time
//
// ═══════════════════════════════════════════════════════════════════════════

var builder = DistributedApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────────────────────────
// MODE 1 — Pre-built Docker images
// Infrastructure you don't own. Aspire pulls and manages the containers.
// ─────────────────────────────────────────────────────────────────────────────

// ── PostgreSQL ───────────────────────────────────────────────────────────────
// LEARNING: AddPostgres() starts a postgres container.
// AddDatabase() creates a logical database inside it.
// WithPgAdmin() spins up the pgAdmin web UI alongside it.
//
// Services reference the database using WithReference(catalogDb).
// Aspire injects the connection string as an environment variable:
//   ConnectionStrings__catalog-db=Host=localhost;Port=xxxxx;Database=catalog;...
// The service reads it with: builder.Configuration.GetConnectionString("catalog-db")
var postgres = builder.AddPostgres("postgres")
    .WithImage("postgres", "17-alpine")   // Pin image tag for reproducibility.
    .WithPgAdmin()                        // Starts pgAdmin at a random port (see dashboard).
    .WithDataVolume("nexa-postgres-data"); // Persists data across container restarts.

var catalogDb = postgres.AddDatabase("catalog-db");

// ── MinIO (S3-compatible Object Storage) ─────────────────────────────────────
// LEARNING: MinIO is not a native Aspire resource (no AddMinIO() exists),
// so we use AddContainer() — the generic "run any Docker image" API.
//
// WithArgs("server", "/data") → MinIO's startup command.
// WithVolume() → persists uploaded files across restarts.
// WithEnvironment() → sets MinIO's root credentials.
// WithHttpEndpoint() → exposes the S3 API port AND the admin console.
//
// Admin console: http://localhost:9001  (user: minioadmin / pass: minioadmin)
// S3 API:        http://localhost:9000  (used by MinioObjectStorageService)
//
// Production swap: change the ServiceURL in appsettings to your AWS S3 endpoint.
// Code in ProductService.cs doesn't change at all.
var minio = builder.AddContainer("minio", "minio/minio", "RELEASE.2025-04-22T22-12-26Z")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithVolume("nexa-minio-data", "/data")
    .WithEnvironment("MINIO_ROOT_USER", "minioadmin")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "minioadmin")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "s3-api")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console");

// ── RabbitMQ ─────────────────────────────────────────────────────────────────
// LEARNING: RabbitMQ is the message broker used by Wolverine from Phase 6+.
// In Phases 2–5, Wolverine uses local (in-process) transport — no broker needed.
// We register it here so the AppHost is ready for Phase 6 without changes.
//
// WithManagementPlugin() → enables the web management UI.
// Management UI: http://localhost:15672  (user: guest / pass: guest)
//
// Phase 6 adds WolverineFx.RabbitMQ to the services and calls:
//   opts.UseRabbitMq(rabbitMqConnectionString)
// That single line is the only code change to switch transports.
var rabbitMq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin()             // Uses rabbitmq:*-management image automatically.
    .WithDataVolume("nexa-rabbitmq-data");

// ── Traefik (Reverse Proxy) ───────────────────────────────────────────────────
// LEARNING: Traefik routes external HTTP traffic to your services.
// AddContainer() is used because there is no native Aspire Traefik resource.
//
// WithBindMount() mounts your local config files into the container.
//   - traefik.yml  → static config (entry points, providers)
//   - dynamic/     → dynamic config (routes, middleware, TLS)
//
// ROUTING TABLE for NexaCommerce:
//   ProductCatalog   → http://traefik:80/api/products/**   (HTTP service ✅)
//   Notifications    → NOT routed (worker, no HTTP surface ❌)
//   ReportScheduler  → NOT routed (worker, no HTTP surface ❌)
//
// Workers communicate only through the message bus — they are invisible to HTTP.
// Routing them through Traefik would be a mistake (they have no endpoints to route to).
//
// Phase 7 adds the Traefik config files to infra/traefik/.
// Uncomment this block in Phase 7.
/*
var traefik = builder.AddContainer("traefik", "traefik", "v3.3")
    .WithArgs(
        "--providers.file.directory=/etc/traefik/dynamic",
        "--providers.file.watch=true",
        "--entryPoints.web.address=:80",
        "--api.dashboard=true",
        "--api.insecure=true")          // Dashboard without auth — dev only!
    .WithBindMount("../../infra/traefik/traefik.yml", "/etc/traefik/traefik.yml", isReadOnly: true)
    .WithBindMount("../../infra/traefik/dynamic", "/etc/traefik/dynamic", isReadOnly: true)
    .WithHttpEndpoint(port: 80,   targetPort: 80,   name: "http")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "dashboard");
*/

// ─────────────────────────────────────────────────────────────────────────────
// MODE 2 — .NET projects built from source
// Services you own. Aspire compiles, runs, and hot-restarts them.
// ─────────────────────────────────────────────────────────────────────────────
//
// LEARNING: AddProject<T>() is the key Aspire API for your own services.
//
//   builder.AddProject<Projects.NexaCommerce_ProductCatalog>("product-catalog")
//
// What Aspire does automatically:
//   1. Compiles the project (or uses the running hot-reload session)
//   2. Starts it as a child process
//   3. Injects OTEL_EXPORTER_OTLP_ENDPOINT → logs/traces appear in dashboard
//   4. WithReference(catalogDb) → injects ConnectionStrings__catalog-db
//   5. WaitFor(catalogDb) → service doesn't start until DB is ready
//   6. Assigns a random port and injects ASPNETCORE_URLS
//
// The generated Projects.NexaCommerce_ProductCatalog type is created by the
// Aspire.AppHost.Sdk from the <ProjectReference> in the .csproj.
// ─────────────────────────────────────────────────────────────────────────────

// ── Phase 4: ProductCatalog web API ──────────────────────────────────────────
// UNCOMMENT after completing Phase 4 and adding the ProjectReference in .csproj.
var catalogApi = builder.AddProject<Projects.NexaCommerce_ProductCatalog>("product-catalog")
    .WithReference(catalogDb)           // → ConnectionStrings__catalog-db
    .WithReference(rabbitMq)            // → ConnectionStrings__rabbitmq (Phase 6)
    .WithEnvironment("Storage__ServiceUrl", minio.GetEndpoint("s3-api"))
    .WaitFor(catalogDb);                // Wait for Postgres to be healthy first.

// ── Phase 5: Notifications worker ────────────────────────────────────────────
// Workers have no HTTP surface — they are NOT exposed through Traefik.
// UNCOMMENT after completing Phase 5.
var notifications = builder.AddProject<Projects.NexaCommerce_Notifications>("notifications")
    .WithReference(rabbitMq)            // Wolverine reads events from RabbitMQ (Phase 6+)
    .WithReference(catalogDb)           // Reads product data for notification content
    .WaitFor(catalogApi);

// ── Phase 6: ReportScheduler worker ──────────────────────────────────────────
// Also a worker — no HTTP surface, not in Traefik.
// UNCOMMENT after completing Phase 6.
/*
var reportScheduler = builder.AddProject<Projects.NexaCommerce_ReportScheduler>("report-scheduler")
    .WithReference(catalogDb)           // Quartz.NET stores its job state in Postgres
    .WithReference(rabbitMq)            // Publishes ReportReadyEvent via Wolverine
    .WaitFor(catalogDb);
*/

// ─────────────────────────────────────────────────────────────────────────────
// MODE 3 — Local npm process
// Non-.NET apps you own. Aspire runs npm and streams logs to the dashboard.
// ─────────────────────────────────────────────────────────────────────────────
//
// LEARNING: AddNpmApp() is Aspire's bridge to the JavaScript ecosystem.
//
//   builder.AddNpmApp("frontend", "../../frontend/nexacommerce-ui")
//
// What Aspire does automatically:
//   1. Runs `npm install` if node_modules is missing (WithNpmPackageInstallation)
//   2. Runs `npm run start`
//   3. Streams stdout/stderr to the Aspire dashboard as structured logs
//   4. WithEnvironment() injects the catalog API URL so the Angular app can call it
//   5. Appears in the dashboard like any other service (health, logs, traces)
//
// This means the Angular team sees the backend URL automatically — no manual config.
// ─────────────────────────────────────────────────────────────────────────────

// ── Phase 11: Angular frontend ────────────────────────────────────────────────
// UNCOMMENT after completing Phase 11.
/*
var frontend = builder.AddNpmApp("frontend", "../../frontend/nexacommerce-ui")
    .WithNpmPackageInstallation()
    .WithEnvironment("CATALOG_API_URL", catalogApi.GetEndpoint("http"))
    .WithHttpEndpoint(port: 4200, targetPort: 4200, env: "PORT");
*/

// ─────────────────────────────────────────────────────────────────────────────
// Build and run the application
// ─────────────────────────────────────────────────────────────────────────────

builder.Build().Run();
