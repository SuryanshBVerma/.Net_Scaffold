# NexaCommerce — .NET 10 Enterprise Learning Scaffold

A production-patterned e-commerce backend built to learn modern .NET 10 architecture hands-on. Every layer is deliberately over-engineered for a small domain so the patterns stand out clearly.

---

## What's inside

| Layer | Technology |
|---|---|
| Orchestration | [.NET Aspire 13](https://learn.microsoft.com/en-us/dotnet/aspire/) |
| API | [FastEndpoints 8](https://fast-endpoints.com/) (REPR pattern) |
| ORM | [EF Core 10](https://learn.microsoft.com/en-us/ef/core/) + PostgreSQL |
| Messaging | [Wolverine 5](https://wolverine.netlify.app/) + RabbitMQ |
| Scheduling | [Quartz.NET 3](https://www.quartz-scheduler.net/) |
| Object Storage | [MinIO](https://min.io/) |
| Reverse Proxy | [Traefik v3](https://traefik.io/) |
| Testing | xUnit v3 · Testcontainers · Playwright |
| CI | GitHub Actions |

---

## Quick start

```bash
git clone https://github.com/SuryanshBVerma/.Net_Scaffold.git
cd .Net_Scaffold

# Start everything via Aspire (Docker required)
dotnet run --project aspire/NexaCommerce.AppHost

# Aspire dashboard  → http://localhost:15888
# Traefik dashboard → http://localhost:8081
# API               → http://localhost:5001
```

---

## Repository layout

```
.
├── aspire/                  # AppHost — local dev orchestration only
├── backend/
│   ├── product-catalog/     # ProductCatalog API (FastEndpoints + EF Core)
│   └── shared-kernel/       # Cross-cutting concerns (results, extensions)
├── frontend/                # Angular app (served via Aspire)
├── tests/
│   ├── common/              # Shared fixtures (PostgreSqlFixture)
│   ├── integration/         # WebApplicationFactory + Testcontainers
│   ├── unit/                # Pure unit tests
│   └── e2e/                 # Playwright smoke tests
└── wiki/                    # GitHub wiki source (separate git remote)
```

---

## Wiki

Full documentation lives in the [GitHub Wiki](https://github.com/SuryanshBVerma/.Net_Scaffold/wiki).

| Page | What you'll learn |
|---|---|
| [Home](https://github.com/SuryanshBVerma/.Net_Scaffold/wiki/Home) | Overview, quick start, design decisions |
| [Architecture](https://github.com/SuryanshBVerma/.Net_Scaffold/wiki/Architecture) | System diagram, component map, port reference |
| [Aspire Orchestration](https://github.com/SuryanshBVerma/.Net_Scaffold/wiki/Aspire-Orchestration) | AppHost walkthrough, config injection, dashboard |
| [FastEndpoints & REPR](https://github.com/SuryanshBVerma/.Net_Scaffold/wiki/FastEndpoints-REPR-Pattern) | Endpoint anatomy, validation, result mapping |
| [EF Core & LINQ](https://github.com/SuryanshBVerma/.Net_Scaffold/wiki/EF-Core-and-LINQ) | DbContext design, all LINQ operators, migrations |
| [Wolverine Messaging](https://github.com/SuryanshBVerma/.Net_Scaffold/wiki/Wolverine-Messaging) | Messages, handlers, in-process vs RabbitMQ |
| [Quartz Job Scheduling](https://github.com/SuryanshBVerma/.Net_Scaffold/wiki/Quartz-Job-Scheduling) | Jobs, IDbContextFactory, DisallowConcurrentExecution |
| [MinIO Object Storage](https://github.com/SuryanshBVerma/.Net_Scaffold/wiki/MinIO-Object-Storage) | Storage abstraction, upload endpoint, console |
| [Traefik Reverse Proxy](https://github.com/SuryanshBVerma/.Net_Scaffold/wiki/Traefik-Reverse-Proxy) | Static/dynamic config, routing rules, HTTPS |
| [Testing Strategy](https://github.com/SuryanshBVerma/.Net_Scaffold/wiki/Testing-Strategy) | Unit → integration → E2E, fixtures, gotchas |
| [CI/CD Pipeline](https://github.com/SuryanshBVerma/.Net_Scaffold/wiki/CI-CD-Pipeline) | GitHub Actions jobs, SDK pinning, E2E in CI |

---

## Tech stack versions

| Package | Version |
|---|---|
| .NET SDK | 10.0.201 |
| .NET Aspire | 13.2.0 |
| FastEndpoints | 8.1.0 |
| EF Core | 10.0.5 |
| Wolverine | 5.24.0 |
| Quartz.NET | 3.18.1 |
| xUnit | v3 3.2.2 |
| Testcontainers | 4.11.0 |
| Playwright | 1.52.0 |
