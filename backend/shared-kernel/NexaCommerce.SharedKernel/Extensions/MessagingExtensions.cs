using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.RabbitMQ;

namespace NexaCommerce.SharedKernel.Extensions;

/// <summary>
/// Configures Wolverine messaging for all NexaCommerce services.
///
/// LEARNING: Transport progression:
///   Phase 2-5: local in-process (no broker, pass no connection string).
///   Phase 6+:  RabbitMQ durable transport (pass rabbitMqConnectionString).
///   Handlers, events, service code: ZERO changes between transports.
/// </summary>
public static class MessagingExtensions
{
    /// <summary>
    /// Registers Wolverine. When <paramref name="rabbitMqConnectionString"/> is
    /// provided, uses RabbitMQ as the durable transport. Otherwise falls back
    /// to the default in-process transport (Phases 2-5).
    ///
    /// LEARNING - handler discovery:
    ///   UseWolverine() scans the entry assembly for any public class with a
    ///   public Handle(TMessage) method. No interface, no attribute needed.
    ///
    /// LEARNING - Phase 6 upgrade (one line change):
    ///   Before: builder.Host.AddMessaging()
    ///   After:  builder.Host.AddMessaging(config.GetConnectionString("rabbitmq"))
    ///   Wolverine calls opts.UseRabbitMq(...).AutoProvision() automatically.
    /// </summary>
    public static IHostBuilder AddMessaging(
        this IHostBuilder hostBuilder,
        string? rabbitMqConnectionString = null)
    {
        hostBuilder.UseWolverine(opts =>
        {
            // LEARNING: RabbitMQ transport (Phase 6+).
            // AutoProvision() creates exchanges + queues if they do not exist.
            if (!string.IsNullOrWhiteSpace(rabbitMqConnectionString))
            {
                opts.UseRabbitMq(new Uri(rabbitMqConnectionString))
                    .AutoProvision();
            }
            // else: default in-process local transport (Phases 2-5).

            opts.Durability.Mode = DurabilityMode.Solo;

            // Human-readable message logging in dev. Remove in production.
            opts.Policies.LogMessageStarting(LogLevel.Debug);
        });

        return hostBuilder;
    }
}