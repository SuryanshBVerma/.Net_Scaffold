# NexaCommerce Scaffold — Build Status

> Auto-updated as each phase completes. Each phase includes a build verification result.

---

## Legend
- ✅ Complete + builds clean
- 🔨 In progress
- ❌ Not started
- ⚠️ Issue (see notes)

---

## Phase Status

| # | Phase | Status | Commit | Build |
|---|-------|--------|--------|-------|
| 1 | Repo Foundation (global.json, CPM, NuGet.Config) | ✅ Complete | `chore: initialise solution...` | ✅ Build succeeded |
| 2 | Shared Kernel Library | ✅ Complete | `feat(shared-kernel): add SharedKernel...` | ✅ Build succeeded |
| 3 | Aspire AppHost (all 3 resource modes) | ✅ Complete | `feat(aspire): add AppHost...` | ✅ Build succeeded |
| 4 | ProductCatalog Web API (EF Core, LINQ, FastEndpoints, Wolverine, MinIO) | ❌ | — | — |
| 5 | Notifications Worker (Wolverine local transport) | ❌ | — | — |
| 6 | ReportScheduler + RabbitMQ Transport Upgrade | ❌ | — | — |
| 7 | Traefik Reverse Proxy | ❌ | — | — |
| 8 | Tests (Unit + Testcontainers + Playwright E2E) | ❌ | — | — |
| 9 | Dockerfiles + docker-compose | ❌ | — | — |
| 10 | Azure DevOps Pipeline Templates | ❌ | — | — |
| 11 | Frontend Placeholder (nexacommerce-ui) | ❌ | — | — |

---

## Phase 1 — Repo Foundation

**Goal:** Every developer clones and gets a working build immediately.

### Files Created
- [x] `.gitignore`
- [x] `global.json` — SDK pinned to `10.0.201` with `rollForward: latestFeature`
- [x] `Directory.Build.props` — Nullable, ImplicitUsings, lock files, test project detection
- [x] `Directory.Packages.props` — All NuGet versions in one place (CPM)
- [x] `NuGet.Config` — nuget.org feed (with `<clear />` for reproducibility)
- [x] `NexaCommerce.slnx` — Solution file (.slnx format, new in .NET 10)

### Key Learning
- Central Package Management (`ManagePackageVersionsCentrally=true`) — one place to bump versions across 10+ projects
- `Directory.Build.props` — MSBuild settings applied to ALL projects automatically
- `global.json` with `rollForward: latestFeature` — CI and local always use same SDK
- Lock files (`RestorePackagesWithLockFile=true`) — reproducible restores, catches version drift

### Build Verification
```
dotnet restore NexaCommerce.slnx
dotnet build NexaCommerce.slnx --no-restore
```
**Result:** ✅ `Build succeeded` — 0 projects, 1 expected warning (no projects to restore yet)

---

## Phase 2 — Shared Kernel Library

**Goal:** One library all services consume. Defines auth, logging, OTEL, messaging, and storage conventions.

### Key Learning
- `IUserContext` — typed identity; inject anywhere instead of reading raw `ClaimsPrincipal`
- `AddNexaCommerceDefaults()` — Serilog + OpenTelemetry + health checks in one call
- `MessagingExtensions.AddMessaging()` — Wolverine local transport (Phases 2–5), swaps to RabbitMQ in Phase 6
- `IObjectStorageService` — MinIO locally, AWS S3 in production, zero code change

**Result:** ✅ `Build succeeded` — 18s clean build, 0 errors, 0 warnings

---

## Phase 3 — Aspire AppHost

**Goal:** Single `dotnet run` boots the entire stack.

### 3 Aspire Resource Modes Demonstrated
1. **Pre-built Docker images** — Postgres, RabbitMQ, MinIO, Traefik (infrastructure you don't own)
2. **Built from .csproj source** — ProductCatalog, Notifications, ReportScheduler (services you own)
3. **Local npm process** — nexacommerce-ui frontend (non-.NET you own)

**Result:** ✅ `Build succeeded` — 13.6s, 0 errors, 0 warnings. Mode 2 and 3 blocks present but commented; activate per phase.

---

## Phase 4 — ProductCatalog Web API

**Goal:** Complete web service with every backend pattern.

### LINQ Patterns Demonstrated (in `ProductService.cs`)
- `Where` — filtering
- `Select` — projection (avoid loading full entity)
- `Include` — EF navigation / SQL JOIN
- `GroupBy` + `Count` + `Average` — aggregation
- `Skip` / `Take` — server-side pagination
- `AnyAsync` / `CountAsync` — existence and count checks
- `.AsNoTracking()` — read-only performance

**Result:** ❌ Not started

---

## Phase 5 — Notifications Worker

**Result:** ❌ Not started

---

## Phase 6 — ReportScheduler + RabbitMQ Transport Upgrade

### LINQ Bulk Operations (in cleanup job)
- `ExecuteDeleteAsync` — bulk DELETE without loading entities
- `ExecuteUpdateAsync` — bulk UPDATE without loading entities

**Result:** ❌ Not started

---

## Phase 7 — Traefik Reverse Proxy

### Routing Table
| Service | Traefik route | Why |
|---|---|---|
| ProductCatalog | ✅ Yes | HTTP API |
| Notifications | ❌ No | Worker, no HTTP surface |
| ReportScheduler | ❌ No | Worker, no HTTP surface |

**Result:** ❌ Not started

---

## Phase 8 — Tests

### Coverage
- Unit tests: `ProductServiceTests.cs` (Moq + Shouldly + InMemory EF)
- Integration tests: Testcontainers (real Postgres + real MinIO)
- E2E: Playwright smoke test against running Aspire stack

### LINQ Assertion Patterns (in integration tests)
- `.ShouldAllBe()` — all items satisfy predicate
- `.ShouldContain()` — specific item present
- ordering verification

**Result:** ❌ Not started

---

## Phase 9 — Dockerfiles + docker-compose

**Result:** ❌ Not started

---

## Phase 10 — Azure DevOps Pipelines

**Result:** ❌ Not started

---

## Phase 11 — Frontend Placeholder

**Result:** ❌ Not started

---

## Known Issues / Notes

| Date | Note |
|------|------|
| Phase 1 start | SDK installed: `10.0.201`. `global.json` uses `rollForward: latestFeature` so any 10.0.x patch works. |
| CPM reminder | After `dotnet new`, always strip ` Version="..."` from generated .csproj files — templates hardcode versions which break CPM. |
| Aspire version | Using `Aspire.AppHost.Sdk 13.2.0` (same as BRS-Stack reference). |
| Wolverine transport | Phases 2–5: local (in-memory). Phase 6: RabbitMQ (one-line change). |
