using NexaCommerce.ProductCatalog.Data;
using NexaCommerce.ProductCatalog.Services;
using NexaCommerce.SharedKernel.Extensions;
using NexaCommerce.SharedKernel.Storage;

// ═══════════════════════════════════════════════════════════════════════════
// NexaCommerce.ProductCatalog — Program.cs
//
// LEARNING: This is intentionally short (~25 lines of actual setup code).
// All complexity is in SharedKernel extension methods. Each method call
// here represents a group of related registrations — open the extension
// method to see the full detail.
//
// Benefits of this pattern:
//   • Every service has an identically structured Program.cs
//   • New team members can read and understand startup in 2 minutes
//   • Cross-cutting concerns can be updated once in SharedKernel
// ═══════════════════════════════════════════════════════════════════════════

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure: logging, OTEL, health checks, IUserContext ───────────
// See SharedKernel/Extensions/ServiceCollectionExtensions.cs for details.
builder.Services.AddNexaCommerceDefaults(builder.Configuration, serviceName: "product-catalog");

// ── JWT Bearer authentication + ASP.NET Core authorization ──────────────
builder.Services.AddNexaCommerceAuth(builder.Configuration);

// ── FastEndpoints + Swagger ──────────────────────────────────────────────
builder.Services.AddNexaFastEndpoints();

// ── Messaging (Wolverine — local transport in Phases 2-5) ───────────────
// Phase 6: swap to RabbitMQ by changing MessagingExtensions.cs (one line).
builder.Host.AddMessaging();

// ── Data layer: CatalogDbContext with PostgreSQL ─────────────────────────
// LEARNING: AddNpgsqlDbContext reads ConnectionStrings__catalog-db injected by Aspire.
// In development without Aspire, set ConnectionStrings:catalog-db in appsettings.json.
builder.AddNpgsqlDbContext<CatalogDbContext>("catalog-db");

// ── Storage: MinIO (S3-compatible) ──────────────────────────────────────
// Reads Storage:ServiceUrl, Storage:AccessKey, Storage:SecretKey from config.
// Aspire injects the MinIO endpoint URL automatically via WithEnvironment().
builder.Services.AddSingleton<IObjectStorageService, MinioObjectStorageService>();

// ── Business logic services ──────────────────────────────────────────────
builder.Services.AddScoped<IProductService, ProductService>();

// ════════════════════════════════════════════════════════════════════════════

var app = builder.Build();

// ── Middleware pipeline (order matters — see SharedKernel for details) ───
app.UseNexaCommerceDefaults();

// ── EF Core: run any pending migrations on startup ───────────────────────
// LEARNING: Safe to call on every boot — MigrateAsync() is a no-op if up-to-date.
// Aspire's WaitFor(catalogDb) ensures Postgres is ready before this runs.
await app.MigrateDatabase<CatalogDbContext>();

app.Run();
