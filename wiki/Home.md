# NexaCommerce — Learning Wiki

> **NexaCommerce** is a fictitious product-management platform built as an enterprise .NET 10 learning scaffold. Every technology choice is intentional and documented here.

---

## What You Will Learn

| Concept | Technology | Wiki Page |
|---|---|---|
| Cloud-native orchestration | .NET Aspire 13 | [Aspire Orchestration](Aspire-Orchestration.md) |
| Minimal API with REPR pattern | FastEndpoints 8 | [FastEndpoints & REPR](FastEndpoints-REPR-Pattern.md) |
| ORM, migrations, advanced LINQ | EF Core 10 + PostgreSQL | [EF Core & LINQ](EF-Core-and-LINQ.md) |
| Message-driven architecture | Wolverine 5 + RabbitMQ | [Wolverine Messaging](Wolverine-Messaging.md) |
| Background job scheduling | Quartz.NET 3 | [Quartz Job Scheduling](Quartz-Job-Scheduling.md) |
| S3-compatible object storage | MinIO | [MinIO Object Storage](MinIO-Object-Storage.md) |
| Reverse proxy & routing | Traefik v3 | [Traefik Reverse Proxy](Traefik-Reverse-Proxy.md) |
| Testing pyramid (unit → E2E) | xUnit v3, Testcontainers, Playwright | [Testing Strategy](Testing-Strategy.md) |
| Continuous integration | GitHub Actions | [CI/CD Pipeline](CI-CD-Pipeline.md) |
| Full system architecture | All of the above | [Architecture](Architecture.md) |

---

## Quick Start

```bash
# Clone
git clone https://github.com/SuryanshBVerma/NexaCommerce.git
cd NexaCommerce

# Start everything with Aspire (requires Docker Desktop)
dotnet run --project aspire/app-host

# Aspire dashboard → http://localhost:15000
# ProductCatalog API → shown in dashboard
# Traefik dashboard → http://localhost:8081
```

**Alternatively — run services individually for development:**
```bash
# Terminal 1 — start Postgres + MinIO + RabbitMQ via docker-compose
docker compose up postgres minio rabbitmq -d

# Terminal 2 — API
cd backend/product-catalog/NexaCommerce.ProductCatalog
dotnet run

# Terminal 3 — Notifications worker
cd backend/notifications/NexaCommerce.Notifications
dotnet run

# Terminal 4 — Report scheduler
cd backend/report-scheduler/NexaCommerce.ReportScheduler
dotnet run
```

---

## Repository Layout

```
NexaCommerce/
├── aspire/app-host/          # .NET Aspire AppHost — orchestrates all services
├── backend/
│   ├── shared-kernel/        # Cross-cutting: auth, health checks, OTEL, messaging bootstrap
│   ├── product-catalog/      # REST API — FastEndpoints, EF Core, MinIO
│   ├── notifications/        # Wolverine message consumer worker
│   └── report-scheduler/     # Quartz.NET background job worker
├── frontend/nexacommerce-ui/ # Angular 20 SPA
├── infrastructure/traefik/   # Traefik static + dynamic config
├── tests/
│   ├── common/               # Shared Testcontainers fixtures
│   ├── integration/          # Real PostgreSQL via Testcontainers
│   └── e2e/                  # Playwright browser smoke tests
├── wiki/                     # ← You are here
├── docker-compose.yml
└── .github/workflows/dotnet.yml
```

---

## Key Design Decisions

### Central Package Management (CPM)
All NuGet versions live in `Directory.Packages.props`. Individual `.csproj` files reference packages **without** a `Version` attribute. This means upgrading a package touches exactly one file.

### SharedKernel Extension Methods
`Program.cs` in every service is ~25 lines. All complexity is in `NexaCommerce.SharedKernel` extension methods (`AddNexaCommerceDefaults`, `AddNexaCommerceAuth`, `AddNexaFastEndpoints`, `AddMessaging`). New services are consistent and readable in minutes.

### Config-driven Aspire AppHost
No literals in `Program.cs` of the AppHost. All ports, tags, and credentials come from `appsettings.json` → `builder.Configuration`. Changing a port or image tag is a single-line config change.

### Load-then-mutate for EF Core
EF Core's InMemory provider (used in unit tests) does not support direct SQL `UPDATE`. All mutations follow the pattern: **load the entity first → modify in memory → `SaveChangesAsync`**. This keeps unit tests and real-DB integration tests consistent.
