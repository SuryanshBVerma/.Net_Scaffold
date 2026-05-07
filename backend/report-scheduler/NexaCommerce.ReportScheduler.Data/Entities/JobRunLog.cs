namespace NexaCommerce.ReportScheduler.Data.Entities;

/// <summary>
/// Audit record for every Quartz job run.
///
/// LEARNING — Why an audit log table?
///   Quartz tracks job state internally (next run, last run time), but it
///   does not store the outcome (success/failure) or any business details.
///   JobRunLog fills that gap: every run is recorded with its result so you
///   can query "which jobs failed in the last 24 hours?" or
///   "how long does the cleanup job take on average?"
///
/// LEARNING — ExecuteDeleteAsync target:
///   In StaleProductCleanupJob, old successful job logs are pruned using:
///     db.JobRunLogs.Where(l => l.StartedAt < cutoff && l.Succeeded)
///                  .ExecuteDeleteAsync()
///   This translates to a single SQL DELETE — no entities are loaded.
/// </summary>
public sealed class JobRunLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Quartz job type name (e.g. "StaleProductCleanupJob").</summary>
    public required string JobName { get; set; }

    public DateTimeOffset StartedAt  { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public bool           Succeeded  { get; set; }

    /// <summary>Error message on failure; null on success.</summary>
    public string? Details { get; set; }

    /// <summary>How long the job took.</summary>
    public TimeSpan Duration => FinishedAt - StartedAt;
}
