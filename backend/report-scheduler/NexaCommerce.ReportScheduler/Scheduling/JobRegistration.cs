using NexaCommerce.ReportScheduler.Jobs;
using Quartz;

namespace NexaCommerce.ReportScheduler.Scheduling;

/// <summary>
/// Reads cron expressions from configuration and registers jobs with Quartz.
///
/// LEARNING — Cron externalized to config:
///   Hard-coding cron expressions in C# requires a recompile and redeploy to
///   change a schedule. Externalizing to appsettings.json lets ops teams
///   adjust schedules via environment variables or Azure App Config — no code
///   change, no redeploy.
///
///   Pattern:
///     appsettings.json  →  "Jobs": { "StaleProductCleanup": "0 0 2 * * ?" }
///     IConfiguration    →  config["Jobs:StaleProductCleanup"]
///     Quartz            →  WithCronSchedule(cronExpression)
///
/// LEARNING — Job vs Trigger:
///   Quartz separates WHAT runs (IJob) from WHEN it runs (ITrigger).
///   One job can have multiple triggers (e.g. cron + manual fire).
///   This class creates one trigger per job.
/// </summary>
public static class JobRegistration
{
    public static IServiceCollection AddScheduledJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddQuartz(q =>
        {
            // LEARNING: UseMicrosoftDependencyInjectionJobFactory()
            // This is the bridge between Quartz and .NET DI.
            // Without it, Quartz creates jobs with Activator.CreateInstance()
            // (no DI injection). With it, all constructor dependencies are resolved
            // from the service container — just like a controller or middleware.
            q.UseMicrosoftDependencyInjectionJobFactory();

            // ── StaleProductCleanupJob ────────────────────────────────────────
            var cleanupKey  = JobKey.Create(nameof(StaleProductCleanupJob));
            var cleanupCron = configuration["Jobs:StaleProductCleanup"]
                              ?? "0 0 2 * * ?"; // Default: 2 AM daily

            q.AddJob<StaleProductCleanupJob>(opts => opts.WithIdentity(cleanupKey));
            q.AddTrigger(opts => opts
                .ForJob(cleanupKey)
                .WithIdentity("StaleProductCleanup-trigger")
                // LEARNING: WithCronSchedule uses standard cron format:
                //   "0 0 2 * * ?" = at 02:00:00 every day
                //   "0 0/5 * * * ?" = every 5 minutes (useful for dev/testing)
                .WithCronSchedule(cleanupCron));

            // ── DailyReportJob ────────────────────────────────────────────────
            var reportKey  = JobKey.Create(nameof(DailyReportJob));
            var reportCron = configuration["Jobs:DailyReport"]
                             ?? "0 0 6 * * ?"; // Default: 6 AM daily

            q.AddJob<DailyReportJob>(opts => opts.WithIdentity(reportKey));
            q.AddTrigger(opts => opts
                .ForJob(reportKey)
                .WithIdentity("DailyReport-trigger")
                .WithCronSchedule(reportCron));
        });

        // LEARNING: AddQuartzHostedService() integrates Quartz with the
        // .NET generic host lifecycle. Quartz starts when the host starts
        // and shuts down gracefully (waiting for running jobs to complete)
        // when the host receives a stop signal (Ctrl+C, SIGTERM).
        //
        // WaitForJobsToComplete=true ensures we don't abandon mid-run jobs
        // when Kubernetes sends SIGTERM before rolling to a new pod.
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
