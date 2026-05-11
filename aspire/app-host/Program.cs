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
// LEARNING — Configuration-driven orchestration:
//   Nothing in this file is hardcoded. All image tags, ports, credentials, and
//   paths are read from appsettings.json (or overridden by environment variables).
//   To upgrade Postgres from 17 to 18: change one line in appsettings.json.
//   To change a port in CI: set an environment variable — no code change needed.
//
// ═══════════════════════════════════════════════════════════════════════════

var builder = DistributedApplication.CreateBuilder(args);

// LEARNING — Reading config in the AppHost:
//   builder.Configuration is a standard IConfiguration backed by:
//     appsettings.json → appsettings.{Environment}.json → environment variables
//   Environment variables override appsettings — use them in CI/CD or Docker.
//   The double-underscore separator maps nested JSON:
//     Traefik__HttpPort=9000  overrides  "Traefik": { "HttpPort": 9000 }
var cfg = builder.Configuration;

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
    // LEARNING: Tag read from config. To upgrade: set Postgres:Tag in appsettings.json.
    .WithImage("postgres", cfg["Postgres:Tag"]!)
    // LEARNING: PgAdmin is optional — disable it in environments without a GUI.
    .WithPgAdmin()
    .WithDataVolume(cfg["Postgres:DataVolume"]!);

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
// Admin console: http://localhost:MinioConsoleTargetPort  (see appsettings.json)
// S3 API:        http://localhost:<random>  (used by MinioObjectStorageService)
//
// Production swap: change Storage:ServiceUrl in the service's appsettings to
// your AWS S3 endpoint. Code in ProductService.cs doesn't change at all.
var minio = builder.AddContainer("minio", "minio/minio", cfg["Minio:Tag"]!)
    .WithArgs("server", "/data", "--console-address", $":{cfg["Minio:ConsoleTargetPort"]}")
    .WithVolume(cfg["Minio:DataVolume"]!, "/data")
    // LEARNING: Credentials read from config — override via User Secrets or env vars
    // in non-dev environments: dotnet user-secrets set "Minio:RootPassword" "secret"
    .WithEnvironment("MINIO_ROOT_USER",     cfg["Minio:RootUser"]!)
    .WithEnvironment("MINIO_ROOT_PASSWORD", cfg["Minio:RootPassword"]!)
    // LEARNING — omitting 'port' (host port) lets Aspire pick a random host port,
    // avoiding conflicts with other processes. The endpoint URL injected into
    // catalogApi via GetEndpoint("s3-api") always reflects the actual assigned port.
    .WithHttpEndpoint(targetPort: int.Parse(cfg["Minio:S3ApiTargetPort"]!),   name: "s3-api")
    .WithHttpEndpoint(targetPort: int.Parse(cfg["Minio:ConsoleTargetPort"]!), name: "console");

// ── RabbitMQ ─────────────────────────────────────────────────────────────────
// LEARNING: RabbitMQ is the message broker used by Wolverine from Phase 6+.
// In Phases 2–5, Wolverine uses local (in-process) transport — no broker needed.
// We register it here so the AppHost is ready for Phase 6 without changes.
//
// WithManagementPlugin() → enables the web management UI.
// Management UI: http://localhost:<random>  (user: guest / pass: guest)
//
// Phase 6 adds WolverineFx.RabbitMQ to the services and calls:
//   opts.UseRabbitMq(rabbitMqConnectionString)
// That single line is the only code change to switch transports.
var rabbitMq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin()
    .WithDataVolume(cfg["RabbitMQ:DataVolume"]!);

// ── Traefik (Reverse Proxy) ───────────────────────────────────────────────────
// LEARNING: Traefik routes external HTTP traffic to your services.
// AddContainer() is used because there is no native Aspire Traefik resource.
//
// WithBindMount() mounts local config files into the container:
//   - traefik.yml  → static config (entry points, providers, log level)
//   - dynamic/     → dynamic config (routes, middleware) — watched + hot-reloaded
//
// ROUTING TABLE for NexaCommerce:
//   ProductCatalog   → http://localhost:{Traefik:HttpPort}/api/products/**  ✅
//   Notifications    → NOT routed (worker, no HTTP surface ❌)
//   ReportScheduler  → NOT routed (worker, no HTTP surface ❌)
//
// Workers communicate only via the message bus — they are invisible to HTTP.
var traefik = builder.AddContainer("traefik", "traefik", cfg["Traefik:Tag"]!)
    // LEARNING — Static config via file (mounted) so the full traefik.yml pattern
    // is demonstrated. The file defines entry points, file provider, and log level.
    .WithBindMount(cfg["Traefik:StaticConfigPath"]!, "/etc/traefik/traefik.yml", isReadOnly: true)
    // LEARNING — Dynamic config directory: Traefik watches this folder and
    // hot-reloads routes/middleware without a container restart.
    .WithBindMount(cfg["Traefik:DynamicConfigPath"]!, "/etc/traefik/dynamic", isReadOnly: true)
    // LEARNING — host port vs targetPort:
    //   targetPort = port Traefik listens on INSIDE the container (80 / 8080).
    //   port       = port Docker binds on the HOST machine (from config).
    //   Using non-standard host ports avoids conflicts with IIS or other services.
    .WithHttpEndpoint(port: int.Parse(cfg["Traefik:HttpPort"]!),      targetPort: 80,   name: "http")
    .WithHttpEndpoint(port: int.Parse(cfg["Traefik:DashboardPort"]!), targetPort: 8080, name: "dashboard");

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
var catalogApi = builder.AddProject<Projects.NexaCommerce_ProductCatalog>("product-catalog")
    .WithReference(catalogDb)           // → ConnectionStrings__catalog-db
    .WithReference(rabbitMq)            // → ConnectionStrings__rabbitmq (Phase 6)
    // LEARNING: GetEndpoint() returns the URL at which the MinIO container is reachable.
    // Aspire injects this as Storage__ServiceUrl into the ProductCatalog process,
    // overriding the value in its appsettings.json at runtime.
    .WithEnvironment("Storage__ServiceUrl", minio.GetEndpoint("s3-api"))
    .WaitFor(catalogDb);

// ── Phase 5: Notifications worker ────────────────────────────────────────────
// Workers have no HTTP surface — they are NOT exposed through Traefik.
var notifications = builder.AddProject<Projects.NexaCommerce_Notifications>("notifications")
    .WithReference(rabbitMq)            // Wolverine reads events from RabbitMQ (Phase 6+)
    .WithReference(catalogDb)           // Reads product data for notification content
    .WaitFor(catalogApi);

// ── Phase 6: ReportScheduler worker ──────────────────────────────────────────
// Also a worker — no HTTP surface, not in Traefik.
var reportScheduler = builder.AddProject<Projects.NexaCommerce_ReportScheduler>("report-scheduler")
    .WithReference(catalogDb)           // Quartz.NET stores its job state in Postgres
    .WithReference(rabbitMq)            // Publishes ReportReadyEvent via Wolverine
    .WaitFor(catalogDb);

// ─────────────────────────────────────────────────────────────────────────────
// MODE 3 — Local npm process
// Non-.NET apps you own. Aspire runs npm and streams logs to the dashboard.
// ─────────────────────────────────────────────────────────────────────────────
//
// LEARNING: AddJavaScriptApp() is Aspire 13's API for npm-based frontends.
//   (Earlier previews used AddNpmApp() — renamed in Aspire 9+/13+.)
//
//   What Aspire does automatically:
//     1. WithNpm(install: true) → runs `npm install` if node_modules is absent
//     2. Runs `npm run start` (prestart hook → generate-env.js → ng serve)
//     3. Streams stdout/stderr to the Aspire dashboard as structured logs
//     4. WithEnvironment() injects CATALOG_API_URL → generate-env.js reads it
//        and writes src/assets/env.js → Angular reads window.__env.CATALOG_API_URL
//     5. WithHttpEndpoint() → the Angular dev server is visible in the dashboard
//
// LEARNING — WithHttpEndpoint for a non-container (npm process) resource:
//   Only 'port' + 'env' are specified — NOT both port and targetPort together.
//   For a direct process there is no Docker network boundary, so a proxy that
//   forwards portX → portX on the same host is invalid. Aspire rejects it.
//   With only 'port' + 'env': Aspire sets PORT=4200, ng serve binds to it directly.

// ── Phase 11: Angular frontend ────────────────────────────────────────────────
var frontend = builder.AddJavaScriptApp("frontend", "../../frontend/nexacommerce-ui", "start")
    .WithNpm(install: true)
    .WithEnvironment("CATALOG_API_URL", catalogApi.GetEndpoint("http"))
    .WaitFor(catalogApi);

// ─────────────────────────────────────────────────────────────────────────────
// Build and run the application
// ─────────────────────────────────────────────────────────────────────────────

builder.Build().Run();

