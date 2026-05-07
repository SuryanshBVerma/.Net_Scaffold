namespace NexaCommerce.ReportScheduler.Messaging;

/// <summary>
/// Published by DailyReportJob to signal that a report has been requested.
///
/// LEARNING — Worker-to-worker messaging:
///   DailyReportJob (ReportScheduler) publishes this event.
///   A future ReportGeneratorHandler could consume it to generate a PDF/CSV.
///   The scheduler doesn't know or care who processes it — loose coupling.
///
///   In Phase 6, this message travels over RabbitMQ (not local transport),
///   demonstrating durable cross-process messaging.
/// </summary>
public sealed record ScheduledReportRequestedEvent(
    Guid   ReportId,
    string ReportType,
    DateTimeOffset RequestedAt);
