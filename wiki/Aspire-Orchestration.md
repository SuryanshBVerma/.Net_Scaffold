# Aspire Orchestration

## What is .NET Aspire?

.NET Aspire is a **local developer orchestration** tool — think of it as a smart `docker-compose` that also understands your .NET services. It:

- Starts all your services (API, workers, databases, queues) in the right order
- Injects connection strings automatically as environment variables
- Provides a real-time dashboard showing logs, traces, and health
- Handles "wait for dependencies" — the API won't start until Postgres is ready

> **Key distinction:** Aspire is for **local development**. In production you use Kubernetes, Azure Container Apps, or docker-compose. The `AppHost` project is never deployed.

---

## AppHost — How It Works

The `AppHost` is a console app that references `Aspire.Hosting` SDK. It describes your entire local stack in C# code:

```csharp
// aspire/app-host/Program.cs
var builder = DistributedApplication.CreateBuilder(args);
var cfg     = builder.Configuration;   // reads appsettings.json

// 1. Infrastructure resources
var postgres = builder.AddPostgres("postgres", ...)
    .WithDataVolume(cfg["Postgres:DataVolume"]!);

var catalogDb = postgres.AddDatabase("catalog-db");

// 2. Application services — reference infrastructure
var catalogApi = builder.AddProject<Projects.NexaCommerce_ProductCatalog>("catalog-api")
    .WithReference(catalogDb)   // injects ConnectionStrings__catalog-db env var
    .WaitFor(catalogDb);        // health-checks Postgres before starting the API

// 3. Frontend — npm-based
builder.AddJavaScriptApp("frontend", "../../frontend/nexacommerce-ui", "start")
    .WithNpm(install: true)
    .WithEnvironment("CATALOG_API_URL", catalogApi.GetEndpoint("http"))
    .WaitFor(catalogApi);

builder.Build().Run();
```

### How Connection Strings Are Injected

When you call `.WithReference(catalogDb)`, Aspire sets this environment variable in the API process:

```
ConnectionStrings__catalog-db=Host=localhost;Port=5432;Database=catalog;...
```

ASP.NET Core's `IConfiguration` maps double-underscores to nested config, so `builder.Configuration.GetConnectionString("catalog-db")` just works — no manual config needed.

---

## Config-driven Design

All values that might change (image tags, ports, passwords) live in `appsettings.json` instead of hardcoded in `Program.cs`:

```json
// aspire/app-host/appsettings.json
{
  "Postgres": { "Tag": "17-alpine", "DataVolume": "nexacommerce-postgres-data" },
  "Minio":    { "Tag": "latest",    "S3ApiTargetPort": 9000, "ConsoleTargetPort": 9001 },
  "RabbitMQ": { "Tag": "3-management-alpine" },
  "Traefik":  { "HttpPort": 8088, "DashboardPort": 8081 },
  "Frontend": { "Port": 4200 }
}
```

Reading config in the AppHost:
```csharp
var cfg = builder.Configuration;

// Correct — always parse int explicitly (no GetValue<int> in AppHost)
int httpPort = int.Parse(cfg["Traefik:HttpPort"]!);

// Correct — use ! to assert non-null (throw fast if config is missing)
string tag = cfg["Postgres:Tag"]!;
```

---

## Aspire Dashboard

After `dotnet run --project aspire/app-host`, open `http://localhost:15000`:

| Tab | What you see |
|---|---|
| **Resources** | All services, their status (Running/Stopped), and their URLs |
| **Console** | Live stdout/stderr from each service |
| **Structured Logs** | Searchable log entries (OpenTelemetry) |
| **Traces** | Distributed traces — follow a request across services |
| **Metrics** | HTTP request rates, EF Core query durations, etc. |

---

## WaitFor — Dependency Ordering

Without `WaitFor`, the API would start before Postgres is ready and crash with a connection error. Aspire solves this:

```csharp
var catalogApi = builder
    .AddProject<Projects.NexaCommerce_ProductCatalog>("catalog-api")
    .WaitFor(catalogDb);   // Aspire polls /health of catalogDb container
```

Aspire polls the resource's health endpoint and only starts the dependent service once it's healthy. This replaces the `depends_on: condition: service_healthy` pattern from docker-compose.

---

## AddJavaScriptApp vs AddNpmApp

In Aspire 13+, `AddNpmApp` was **renamed** to `AddJavaScriptApp`:

```csharp
// ✅ Correct (Aspire 13+)
builder.AddJavaScriptApp("frontend", "../../frontend/nexacommerce-ui", "start")
       .WithNpm(install: true);

// ❌ Old — throws MissingMethodException in Aspire 13
builder.AddNpmApp("frontend", "../../frontend/nexacommerce-ui");
```

---

## Frontend Endpoint Caveat

For non-container resources (like an npm dev server), `WithHttpEndpoint` must use `port` + `env` only — **not** `targetPort`:

```csharp
// ✅ Correct
.WithHttpEndpoint(port: 4200, env: "PORT")

// ❌ Throws InvalidOperationException — targetPort only for containers
.WithHttpEndpoint(port: 4200, targetPort: 4200, env: "PORT")
```
