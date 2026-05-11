# Quartz.NET Job Scheduling

## What is Quartz.NET?

Quartz.NET is a battle-tested .NET job scheduler. It supports:
- Cron expressions and interval-based triggers
- Persistent job stores (jobs survive restarts)
- Cluster-aware execution (only one node runs the job at a time)
- Misfire handling (what happens if the server was down when the job was due)

In NexaCommerce it runs `StaleProductCleanupJob` — a periodic background task that deactivates products nobody has updated in 90 days.

---

## Job Implementation

```csharp
// [DisallowConcurrentExecution] = Quartz will NOT start a second instance of
// this job if the previous run is still executing. Critical for DB-mutation jobs.
[DisallowConcurrentExecution]
public sealed class StaleProductCleanupJob(
    IDbContextFactory<CatalogDbContext> dbFactory,
    IMessageBus                         bus,
    ILogger<StaleProductCleanupJob>     logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        // Use IDbContextFactory (not IDbContext directly) because Quartz jobs are
        // singletons — injecting a scoped DbContext would cause a lifetime conflict.
        await using var db = await dbFactory.CreateDbContextAsync(context.CancellationToken);

        var cutoff    = DateTimeOffset.UtcNow.AddDays(-90);
        var threshold = DateTimeOffset.UtcNow.AddDays(-30);

        // Load-then-mutate: required for InMemory compatibility in unit tests
        var stale = await db.Products
            .Where(p => p.IsActive && p.UpdatedAt < cutoff)
            .ToListAsync(context.CancellationToken);

        foreach (var product in stale)
            product.IsActive = false;

        // Write audit log entry
        db.JobLogs.Add(new JobLog
        {
            JobName   = nameof(StaleProductCleanupJob),
            RunAt     = DateTimeOffset.UtcNow,
            Success   = true,
            Message   = $"Deactivated {stale.Count} stale products."
        });

        await db.SaveChangesAsync(context.CancellationToken);

        // Prune old successful log entries — keep logs lean
        var oldLogs = await db.JobLogs
            .Where(l => l.Success && l.RunAt < threshold)
            .ToListAsync(context.CancellationToken);
        db.JobLogs.RemoveRange(oldLogs);
        await db.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation("StaleProductCleanup: deactivated {Count} products.", stale.Count);
    }
}
```

---

## Why IDbContextFactory Instead of IDbContext?

Quartz jobs are registered as **singletons** (one instance shared across all executions). `DbContext` is **scoped** (one instance per request/operation).

Injecting a scoped service into a singleton causes a **captive dependency** — the scoped object lives as long as the singleton, breaking its intended lifecycle.

`IDbContextFactory<T>` is singleton-safe because it **creates a new DbContext per call**:

```csharp
// ✅ Correct — creates a fresh, short-lived DbContext per job execution
await using var db = await dbFactory.CreateDbContextAsync();

// ❌ Wrong — DbContext lifetime is tied to the job singleton (memory leak + threading bugs)
public StaleProductCleanupJob(CatalogDbContext db) { ... }
```

---

## Registering the Job

```csharp
// backend/report-scheduler/NexaCommerce.ReportScheduler/Program.cs
services.AddQuartz(q =>
{
    var jobKey = new JobKey(nameof(StaleProductCleanupJob));

    q.AddJob<StaleProductCleanupJob>(opts => opts.WithIdentity(jobKey));

    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithIdentity($"{nameof(StaleProductCleanupJob)}-trigger")
        .WithSimpleSchedule(s => s
            .WithIntervalInHours(1)
            .RepeatForever()));
});

services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
```

`WaitForJobsToComplete = true` means that on graceful shutdown (`SIGTERM`), the host waits for any currently-running job to finish before exiting. This prevents partial DB writes.

---

## DisallowConcurrentExecution

Without this attribute, if a job takes longer than its interval, Quartz starts a second instance while the first is still running. For a job that writes to the database, this causes:

- Duplicate audit log entries
- Race conditions on the `IsActive` flag
- Potential deadlocks

`[DisallowConcurrentExecution]` tells Quartz to skip the trigger if the job is already running. The missed execution is handled according to the misfire policy (default: run once when the job finishes).

---

## Job Logs — Audit Trail

`JobLog` entity in `SchedulerDbContext` records every execution:

```csharp
public class JobLog
{
    public Guid             Id      { get; set; }
    public string           JobName { get; set; } = string.Empty;
    public DateTimeOffset   RunAt   { get; set; }
    public bool             Success { get; set; }
    public string?          Message { get; set; }
}
```

The cleanup job itself prunes logs older than 30 days that were successful — failed logs are kept indefinitely for debugging.
