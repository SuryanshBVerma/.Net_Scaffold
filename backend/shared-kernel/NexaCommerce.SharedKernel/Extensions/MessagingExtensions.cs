using Microsoft.Extensions.Hosting;
using Wolverine;

namespace NexaCommerce.SharedKernel.Extensions;

/// <summary>
/// Configures Wolverine messaging for all NexaCommerce services.
///
/// ┌────────────────────────────────────────────────────────────────────┐
/// │  LEARNING — Wolverine transport progression                        │
/// │                                                                    │
/// │  Phase 2–5: Local (in-memory) transport                           │
/// │    • No message broker needed                                      │
/// │    • Messages delivered within the same process                    │
/// │    • Perfect for learning the publish/handle pattern               │
/// │    • Wolverine discovers handlers by convention: any public        │
/// │      method named Handle(SomeMessage msg) is registered            │
/// │                                                                    │
/// │  Phase 6: RabbitMQ transport (one-line swap)                      │
/// │    • Add WolverineFx.RabbitMQ NuGet package                       │
/// │    • Change UseRabbitMq(...) call below                            │
/// │    • Handlers, publishers, services: ZERO changes                  │
/// │    • Wolverine serialises messages as JSON on the wire             │
/// │    • Guarantees at-least-once delivery with inbox/outbox           │
/// └────────────────────────────────────────────────────────────────────┘
/// </summary>
public static class MessagingExtensions
{
    /// <summary>
    /// Registers Wolverine with local (in-process) transport.
    ///
    /// LEARNING: UseWolverine() scans the calling assembly for message handlers.
    /// Convention: any class with a public Handle(TMessage) method is a handler.
    /// No interface to implement, no attribute to add.
    ///
    /// Example handler discovered automatically:
    ///   public class ProductCreatedHandler
    ///   {
    ///       public Task Handle(ProductCreatedEvent evt, CancellationToken ct) { ... }
    ///   }
    ///
    /// Phase 6 swap (no handler changes needed):
    ///   Replace:  opts.UseRabbitMq(rabbitUri)
    ///             .AutoProvision()  // creates exchanges + queues if missing
    ///             .AutoPurgeOnStartup();  // dev-only: clears stale messages
    /// </summary>
    public static IHostBuilder AddMessaging(
        this IHostBuilder hostBuilder,
        string? applicationAssemblyName = null)
    {
        hostBuilder.UseWolverine(opts =>
        {
            // LEARNING: Handler discovery. By default Wolverine scans the entry
            // assembly. If SharedKernel is the entry (it shouldn't be), specify
            // the service assembly explicitly via applicationAssemblyName.
            // In Phase 6, add: opts.UseRabbitMq(new Uri(connectionString));

            // Wolverine automatically uses durable inbox/outbox when EF Core
            // is registered — messages are stored in the DB and published after
            // the transaction commits, guaranteeing at-least-once delivery.
            opts.Durability.Mode = DurabilityMode.Solo;

            // Human-readable message logging in development.
            // LEARNING: Remove in production — impacts throughput.
            opts.Policies.LogMessageStarting(LogLevel.Debug);
        });

        return hostBuilder;
    }
}
