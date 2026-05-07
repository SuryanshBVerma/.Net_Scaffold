namespace NexaCommerce.Notifications.Services;

/// <summary>
/// Stub implementation that logs notifications instead of sending real messages.
///
/// LEARNING — Stub vs Mock:
///   A stub is a real implementation that does nothing (or logs).
///   It satisfies the interface contract without external side-effects.
///   Useful in dev/test environments so the worker can run without a mail server.
///
///   Replace this class in production with a real sender that calls
///   SendGrid, Twilio, Firebase, etc. The handlers don't change at all.
///
/// LEARNING — ILogger<T> generic parameter:
///   The T is used only as a category name for log filtering.
///   It does NOT add any coupling to the implementation type —
///   it just lets you filter "NexaCommerce.Notifications.Services.NotificationSender"
///   in your logging config.
/// </summary>
public sealed class NotificationSender(ILogger<NotificationSender> logger) : INotificationSender
{
    public Task SendProductCreatedAsync(string productName, decimal price, string categoryName, CancellationToken ct = default)
    {
        // LEARNING: Structured logging with named placeholders.
        // Serilog/the logging pipeline captures {ProductName}, {Price}, {Category}
        // as first-class properties — not just string interpolation.
        // This lets you filter/query logs like:
        //   SELECT * FROM logs WHERE ProductName = 'Wireless Keyboard'
        logger.LogInformation(
            "NOTIFICATION [product-created]: {ProductName} in {Category} at {Price:C}",
            productName, categoryName, price);

        return Task.CompletedTask;
    }

    public Task SendProductDeletedAsync(string productName, CancellationToken ct = default)
    {
        logger.LogInformation(
            "NOTIFICATION [product-deleted]: {ProductName} has been removed from the catalog",
            productName);

        return Task.CompletedTask;
    }
}
