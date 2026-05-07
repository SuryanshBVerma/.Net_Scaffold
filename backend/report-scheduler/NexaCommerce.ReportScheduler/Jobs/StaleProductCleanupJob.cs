using Microsoft.EntityFrameworkCore;
using NexaCommerce.ReportScheduler.Data;
using NexaCommerce.ReportScheduler.Data.Entities;
using Quartz;

namespace NexaCommerce.ReportScheduler.Jobs;

/// <summary>
/// Quartz job that prunes stale data and archives old job logs.
///
/// LEARNING — [DisallowConcurrentExecution]:
///   Without this attribute, Quartz can fire a new instance of the job
///   while the previous run is still executing (e.g. if it takes longer
///   than the cron interval). This attribute tells Quartz to skip the
///   new fire if the old one is still running. Essential for database
///   mutation jobs to prevent overlapping transactions.
///
/// LEARNING — IJob interface:
///   Quartz discovers jobs by interface. The scheduler calls Execute(context).
///   Dependencies are resolved from the DI container automatically because
///   we use AddQuartz(q => q.UseMicrosoftDependencyInjectionJobFactory()).
///
/// LEARNING — IDbContextFactory[T] in background jobs:
///   Never inject DbContext directly into a long-lived service or job.
///   DbContext is scoped; Quartz jobs are transient-like but run outside
///   an HTTP request scope. Use IDbContextFactory[T] to create a fresh
///   context with its own connection per execution.
///
/// LINQ LEARNING demonstrated here:
///   • ExecuteDeleteAsync — single SQL DELETE, no entity loading
///   • ExecuteUpdateAsync — single SQL UPDATE, no entity loading
///   • Where + CountAsync  — count before and after for audit
/// </summary>
[DisallowConcurrentExecution]
public sealed class StaleProductCleanupJob(
    IDbContextFactory<SchedulerDbContext> dbFactory,
    ILogger<StaleProductCleanupJob> logger) : IJob
{
    // Threshold: flag products not updated in this many days as inactive.
    // In production this comes from appsettings.json via IConfiguration.
    private const int StaleThresholdDays    = 90;
    private const int LogRetentionDays      = 30;

    public async Task Execute(IJobExecutionContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var ct = context.CancellationToken;

        logger.LogInformation("StaleProductCleanupJob starting at {StartedAt}", startedAt);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var cutoff     = DateTimeOffset.UtcNow.AddDays(-StaleThresholdDays);
            var logCutoff  = DateTimeOffset.UtcNow.AddDays(-LogRetentionDays);

            // ── 1. Deactivate stale products ──────────────────────────────────
            // LINQ LEARNING: Load the matching entities, mutate, then SaveChanges.
            // With a real Postgres provider you would use ExecuteUpdateAsync for
            // a single-SQL UPDATE without loading entities into memory:
            //
            //   await db.Products
            //       .Where(p => p.IsActive && p.UpdatedAt < cutoff)
            //       .ExecuteUpdateAsync(s => s
            //           .SetProperty(p => p.IsActive, false)
            //           .SetProperty(p => p.UpdatedAt, DateTimeOffset.UtcNow), ct);
            //
            // The load-and-save approach below is compatible with all providers
            // (including InMemory used in tests) and is fine for moderate data volumes.
            var staleProducts = await db.Products
                .Where(p => p.IsActive && p.UpdatedAt < cutoff)
                .ToListAsync(ct);

            var now = DateTimeOffset.UtcNow;
            foreach (var p in staleProducts)
            {
                p.IsActive  = false;
                p.UpdatedAt = now;
            }

            var deactivated = staleProducts.Count;

            logger.LogInformation("Deactivated {Count} stale products older than {Days} days",
                deactivated, StaleThresholdDays);

            // ── 2. Prune old successful job log entries ────────────────────────
            // LINQ LEARNING: Similar to step 1 — load then remove.
            // With Postgres, a single-SQL DELETE is possible via ExecuteDeleteAsync:
            //
            //   await db.JobRunLogs
            //       .Where(l => l.StartedAt < logCutoff && l.Succeeded)
            //       .ExecuteDeleteAsync(ct);
            //
            // Only successful logs are pruned; failed logs are kept for investigation.
            var oldLogs = await db.JobRunLogs
                .Where(l => l.StartedAt < logCutoff && l.Succeeded)
                .ToListAsync(ct);

            db.JobRunLogs.RemoveRange(oldLogs);
            var prunedLogs = oldLogs.Count;

            logger.LogInformation("Pruned {Count} job log entries older than {Days} days",
                prunedLogs, LogRetentionDays);

            // ── 3. Write audit log entry for THIS run ─────────────────────────
            db.JobRunLogs.Add(new JobRunLog
            {
                JobName    = nameof(StaleProductCleanupJob),
                StartedAt  = startedAt,
                FinishedAt = DateTimeOffset.UtcNow,
                Succeeded  = true,
                Details    = $"Deactivated={deactivated}, PrunedLogs={prunedLogs}"
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "StaleProductCleanupJob failed");

            // Record the failure so it's visible in the audit log.
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.JobRunLogs.Add(new JobRunLog
            {
                JobName    = nameof(StaleProductCleanupJob),
                StartedAt  = startedAt,
                FinishedAt = DateTimeOffset.UtcNow,
                Succeeded  = false,
                Details    = ex.Message
            });
            await db.SaveChangesAsync(CancellationToken.None);

            // Re-throw so Quartz records the failure and applies retry policy.
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
