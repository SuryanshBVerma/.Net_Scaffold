using Microsoft.EntityFrameworkCore;
using NexaCommerce.ReportScheduler.Data;
using NexaCommerce.ReportScheduler.Scheduling;
using NexaCommerce.SharedKernel.Extensions;
using Serilog;

// ═══════════════════════════════════════════════════════════════════════════
// NexaCommerce.ReportScheduler — Program.cs
// ═══════════════════════════════════════════════════════════════════════════
//
// THE FILE TO STUDY IN PHASE 6.
//
// This is a Worker Service with two responsibilities:
//   1. Run Quartz.NET cron jobs (StaleProductCleanupJob, DailyReportJob)
//   2. Publish events to RabbitMQ via Wolverine
//
// WHAT IS DIFFERENT FROM PHASES 2–5 (local transport):
//   AddMessaging() is called with a RabbitMQ connection string.
//   MessagingExtensions.cs detects this and calls opts.UseRabbitMq().
//   The handlers in Notifications and the jobs here do not change at all.
//   Only the transport layer is swapped — proving the abstraction works.
//
// WHY IDbContextFactory<T> AND NOT DbContext DIRECTLY?
//   Quartz jobs run outside the HTTP request scope. DbContext is designed
//   for short-lived per-request usage. Creating a DbContext inside the
//   job (via IDbContextFactory) gives each execution its own connection,
//   its own transaction scope, and avoids threading issues.
//
// ═══════════════════════════════════════════════════════════════════════════

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        cfg.AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", optional: true);
        cfg.AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        // ── Serilog ───────────────────────────────────────────────────────────
        services.AddSerilog(lc => lc
            .ReadFrom.Configuration(config)
            .Enrich.FromLogContext()
            .WriteTo.Console());

        // ── EF Core (SchedulerDbContext) ──────────────────────────────────────
        // LEARNING: IDbContextFactory<T> is the correct pattern for background
        // jobs. Each Quartz job execution creates a fresh DbContext via the factory,
        // avoiding shared state between concurrent/sequential runs.
        var connStr = config.GetConnectionString("scheduler-db") ?? string.Empty;
        services.AddDbContextFactory<SchedulerDbContext>(options =>
            options.UseNpgsql(connStr));

        // ── Quartz jobs ───────────────────────────────────────────────────────
        // LEARNING: JobRegistration.AddScheduledJobs() reads cron expressions
        // from appsettings.json and registers StaleProductCleanupJob + DailyReportJob.
        services.AddScheduledJobs(config);
    })
    // ── Wolverine with RabbitMQ transport ─────────────────────────────────────
    // LEARNING: This is the Phase 6 upgrade. AddMessaging() now receives the
    // RabbitMQ connection string, switching from local (in-process) transport
    // to durable RabbitMQ messaging. The job code that calls bus.PublishAsync()
    // is identical — only the transport changes.
    .AddMessaging(rabbitMqConnectionString:
        new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddJsonFile("appsettings.json", optional: true)
            .Build()
            .GetConnectionString("rabbitmq"));

builder.Build().Run();
