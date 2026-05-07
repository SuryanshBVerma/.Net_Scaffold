using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexaCommerce.ProductCatalog.Data.Entities;
using NexaCommerce.ReportScheduler.Data;
using NexaCommerce.ReportScheduler.Data.Entities;
using NexaCommerce.ReportScheduler.Jobs;
using Quartz;
using Shouldly;
using Xunit;

namespace NexaCommerce.ReportScheduler.Tests.Jobs;

public sealed class StaleProductCleanupJobTests
{
    private readonly DbContextOptions<SchedulerDbContext> _options;
    private readonly StaleProductCleanupJob _sut;

    private static readonly Guid ElectronicsId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    public StaleProductCleanupJobTests()
    {
        _options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var seed = new SchedulerDbContext(_options);
        seed.Categories.Add(new Category { Id = ElectronicsId, Name = "Electronics" });
        seed.Products.AddRange(
            new Product
            {
                Id = Guid.NewGuid(), Name = "Old Gadget", Description = "stale",
                Price = 9.99m, CategoryId = ElectronicsId, IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-120),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-120)
            },
            new Product
            {
                Id = Guid.NewGuid(), Name = "New Gadget", Description = "fresh",
                Price = 49.99m, CategoryId = ElectronicsId, IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-5)
            });
        seed.JobRunLogs.AddRange(
            new JobRunLog
            {
                JobName = "OldJob", Succeeded = true,
                StartedAt  = DateTimeOffset.UtcNow.AddDays(-40),
                FinishedAt = DateTimeOffset.UtcNow.AddDays(-40).AddSeconds(5)
            },
            new JobRunLog
            {
                JobName = "OldJob", Succeeded = false, Details = "Some error",
                StartedAt  = DateTimeOffset.UtcNow.AddDays(-2),
                FinishedAt = DateTimeOffset.UtcNow.AddDays(-2).AddSeconds(1)
            });
        seed.SaveChanges();

        // Factory creates a fresh context each call, sharing the same InMemory DB.
        var factoryMock = new Mock<IDbContextFactory<SchedulerDbContext>>();
        factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(() => new SchedulerDbContext(_options));

        _sut = new StaleProductCleanupJob(factoryMock.Object, NullLogger<StaleProductCleanupJob>.Instance);
    }

    private SchedulerDbContext NewDb() => new(_options);

    private static IJobExecutionContext JobCtx() =>
        Mock.Of<IJobExecutionContext>(c => c.CancellationToken == CancellationToken.None);

    [Fact]
    public async Task Execute_should_deactivate_stale_products()
    {
        await _sut.Execute(JobCtx());

        await using var db = NewDb();
        var stale = await db.Products.FirstAsync(p => p.Name == "Old Gadget");
        stale.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Execute_should_not_deactivate_fresh_products()
    {
        await _sut.Execute(JobCtx());

        await using var db = NewDb();
        var fresh = await db.Products.FirstAsync(p => p.Name == "New Gadget");
        fresh.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Execute_should_prune_old_successful_job_logs()
    {
        await _sut.Execute(JobCtx());

        await using var db = NewDb();
        var logs = await db.JobRunLogs.ToListAsync();
        logs.ShouldNotContain(l => l.JobName == "OldJob" && l.Succeeded
            && l.StartedAt < DateTimeOffset.UtcNow.AddDays(-35));
    }

    [Fact]
    public async Task Execute_should_keep_failed_job_logs()
    {
        await _sut.Execute(JobCtx());

        await using var db = NewDb();
        var failedLog = await db.JobRunLogs.FirstOrDefaultAsync(l => !l.Succeeded);
        failedLog.ShouldNotBeNull();
    }

    [Fact]
    public async Task Execute_should_write_audit_log_entry()
    {
        await _sut.Execute(JobCtx());

        await using var db = NewDb();
        var auditLog = await db.JobRunLogs
            .FirstOrDefaultAsync(l => l.JobName == nameof(StaleProductCleanupJob) && l.Succeeded);
        auditLog.ShouldNotBeNull();
    }
}