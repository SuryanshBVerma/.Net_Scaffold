using NexaCommerce.ProductCatalog.Messaging;
using NexaCommerce.Notifications.Services;

namespace NexaCommerce.Notifications.Handlers;

/// <summary>
/// Wolverine message handler for ProductDeletedEvent.
///
/// LEARNING — Idempotency in event handlers:
///   A handler may be called more than once for the same message if the broker
///   re-delivers it (e.g. after a crash before acknowledgement).
///   This handler is naturally idempotent — sending a duplicate notification
///   is annoying but not catastrophic. For financial operations, you would
///   store a processed-message-id and skip duplicates.
///
/// LEARNING — Thin handlers, rich services:
///   The handler itself does nothing except delegate to INotificationSender.
///   All logic lives in the service, making the handler trivially testable
///   by just checking the sender was called.
/// </summary>
public sealed class ProductDeletedHandler(
    INotificationSender sender,
    ILogger<ProductDeletedHandler> logger)
{
    public async Task Handle(ProductDeletedEvent evt, CancellationToken ct)
    {
        logger.LogInformation(
            "Handling ProductDeletedEvent for product {ProductId} ({ProductName})",
            evt.ProductId, evt.ProductName);

        await sender.SendProductDeletedAsync(evt.ProductName, ct);
    }
}
