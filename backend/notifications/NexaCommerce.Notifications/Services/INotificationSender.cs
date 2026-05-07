namespace NexaCommerce.Notifications.Services;

/// <summary>
/// Abstraction for sending notifications to external channels.
///
/// LEARNING — Interface segregation in a worker service:
///   The handler knows WHAT to notify (the event), but not HOW (email, SMS,
///   push). This interface is the seam between "business logic" (handlers)
///   and "infrastructure" (the actual sending mechanism).
///
///   In production you would inject:
///     • SmtpNotificationSender  → email via SMTP / SendGrid
///     • SlackNotificationSender → Slack webhook
///     • PushNotificationSender  → FCM / APNS
///   Without changing the handler code at all.
/// </summary>
public interface INotificationSender
{
    Task SendProductCreatedAsync(string productName, decimal price, string categoryName, CancellationToken ct = default);
    Task SendProductDeletedAsync(string productName, CancellationToken ct = default);
}
