using Microsoft.EntityFrameworkCore;
using NexaCommerce.ProductCatalog.Data;
using NexaCommerce.ProductCatalog.Data.Entities;
using NexaCommerce.ReportScheduler.Data.Entities;

namespace NexaCommerce.ReportScheduler.Data;

/// <summary>
/// EF Core DbContext for the ReportScheduler service.
///
/// LEARNING — Multiple DbContexts in one solution:
///   Each service owns its schema. The ReportScheduler does NOT use
///   CatalogDbContext directly — it reads Product/Category data through
///   a separate context instance scoped to its own schema access pattern.
///
///   In a true microservice deployment the Product table would live in
///   a different database entirely. Here both contexts point at the same
///   PostgreSQL server (sharing a connection string) for simplicity,
///   but you can see the ownership boundary in code.
///
/// LEARNING — IDbContextFactory[T]:
///   Background jobs (Quartz) must NOT use a singleton or shared DbContext.
///   Use IDbContextFactory[T] to create a fresh context per job execution:
///     await using var db = await factory.CreateDbContextAsync(ct);
///   This is the correct pattern for any non-request-scoped code path.
/// </summary>
public class SchedulerDbContext(DbContextOptions<SchedulerDbContext> options) : DbContext(options)
{
    // ── Scheduler-owned tables ────────────────────────────────────────────────

    public DbSet<JobRunLog> JobRunLogs => Set<JobRunLog>();

    // ── Read-only access to ProductCatalog tables ─────────────────────────────
    // LEARNING: The ReportScheduler reads product/category data for cleanup
    // and reporting. It uses the same physical tables as CatalogDbContext but
    // through its own context — keeping the service boundary explicit.

    public DbSet<Product>  Products   => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── JobRunLog ─────────────────────────────────────────────────────────
        modelBuilder.Entity<JobRunLog>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.JobName).HasMaxLength(200).IsRequired();

            // LEARNING: Index on StartedAt + Succeeded enables efficient pruning:
            //   WHERE StartedAt < @cutoff AND Succeeded = true
            e.HasIndex(l => new { l.StartedAt, l.Succeeded });

            e.Ignore(l => l.Duration); // computed property — not a column
        });

        // ── Product / Category ────────────────────────────────────────────────
        // LEARNING: These entities are owned by CatalogDbContext. SchedulerDbContext
        // maps them to the same tables but does NOT own migrations for them.
        // We use modelBuilder.Entity<T>() in read-only mode — no HasData seeds here.
        modelBuilder.Entity<Product>(e =>
        {
            e.Property(p => p.Price).HasPrecision(18, 4);
            e.HasOne(p => p.Category).WithMany(c => c.Products).HasForeignKey(p => p.CategoryId);
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.HasMany(c => c.Products).WithOne(p => p.Category).HasForeignKey(p => p.CategoryId);
        });
    }
}
