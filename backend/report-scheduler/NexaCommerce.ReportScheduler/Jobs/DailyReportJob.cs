using Microsoft.EntityFrameworkCore;
using NexaCommerce.ReportScheduler.Data;
using NexaCommerce.ReportScheduler.Data.Entities;
using NexaCommerce.ReportScheduler.Messaging;
using Quartz;
using Wolverine;

namespace NexaCommerce.ReportScheduler.Jobs;

/// <summary>
/// Quartz job that fires on a daily schedule and publishes a report request event.
///
/// LEARNING — Jobs publishing messages:
///   This job doesn't generate the report itself — it publishes an event.
///   This keeps the job small and testable. The actual report generation
///   (PDF, CSV, email) is handled by a Wolverine handler in a separate service.
///
/// LEARNING — Wolverine from a Quartz job:
///   IMessageBus is available via DI injection, just like in a web controller.
///   Publishing here sends the message to RabbitMQ (Phase 6), which delivers
///   it to any subscribed handler — even in a different process.
///
/// LEARNING — [DisallowConcurrentExecution] on this job:
///   A daily report triggered twice simultaneously could produce duplicate
///   reports and incur double the storage/compute cost. Prevent with this attribute.
/// </summary>
[DisallowConcurrentExecution]
public sealed class DailyReportJob(
    IDbContextFactory<SchedulerDbContext> dbFactory,
    IMessageBus bus,
    ILogger<DailyReportJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var ct = context.CancellationToken;

        logger.LogInformation("DailyReportJob starting at {StartedAt}", startedAt);

        try
        {
            // LEARNING: Publish via Wolverine. In Phase 6, this goes over RabbitMQ.
            // Any service with a matching Handle(ScheduledReportRequestedEvent) handler
            // will receive it. No direct dependency on that service.
            var evt = new ScheduledReportRequestedEvent(
                ReportId:    Guid.NewGuid(),
                ReportType:  "DailyProductSummary",
                RequestedAt: startedAt);

            await bus.PublishAsync(evt);

            logger.LogInformation("Published ScheduledReportRequestedEvent {ReportId}", evt.ReportId);

            // Record the job run.
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.JobRunLogs.Add(new JobRunLog
            {
                JobName    = nameof(DailyReportJob),
                StartedAt  = startedAt,
                FinishedAt = DateTimeOffset.UtcNow,
                Succeeded  = true,
                Details    = $"Published report request {evt.ReportId}"
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DailyReportJob failed");

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.JobRunLogs.Add(new JobRunLog
            {
                JobName    = nameof(DailyReportJob),
                StartedAt  = startedAt,
                FinishedAt = DateTimeOffset.UtcNow,
                Succeeded  = false,
                Details    = ex.Message
            });
            await db.SaveChangesAsync(CancellationToken.None);

            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
