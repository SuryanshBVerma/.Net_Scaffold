using NexaCommerce.ProductCatalog.Messaging;
using NexaCommerce.Notifications.Services;

namespace NexaCommerce.Notifications.Handlers;

/// <summary>
/// Wolverine message handler for ProductCreatedEvent.
///
/// LEARNING — Wolverine handler discovery by convention:
///   Any public class with a public method named Handle(SomeMessage) is
///   automatically discovered as a handler. No registration, no attribute,
///   no base class needed.
///
///   Wolverine calls this when a ProductCreatedEvent arrives on the local
///   (in-process) transport (Phases 2–5). In Phase 6, the same handler
///   works unchanged with RabbitMQ — only the transport registration changes.
///
/// LEARNING — Constructor injection in handlers:
///   Wolverine resolves handlers from the DI container for each message,
///   so all registered services are available via constructor injection.
///   The handler is effectively scoped per message.
///
/// LEARNING — Why not use IHostedService for this?
///   IHostedService is for long-running loops (polling, timers).
///   Wolverine handlers are invoked on demand when a message arrives.
///   This is more efficient — no CPU wasted when the queue is empty.
/// </summary>
public sealed class ProductCreatedHandler(
    INotificationSender sender,
    ILogger<ProductCreatedHandler> logger)
{
    // LEARNING: Wolverine discovers this method by the name "Handle" and the
    // first parameter type (ProductCreatedEvent). The CancellationToken is
    // optional — Wolverine injects it automatically when present.
    public async Task Handle(ProductCreatedEvent evt, CancellationToken ct)
    {
        logger.LogInformation(
            "Handling ProductCreatedEvent for product {ProductId} ({ProductName})",
            evt.ProductId, evt.ProductName);

        await sender.SendProductCreatedAsync(evt.ProductName, evt.Price, evt.CategoryName, ct);
    }
}
