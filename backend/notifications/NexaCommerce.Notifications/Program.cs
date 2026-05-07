using NexaCommerce.Notifications.Services;
using NexaCommerce.SharedKernel.Extensions;
using Serilog;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        services.AddSerilog(lc => lc
            .ReadFrom.Configuration(ctx.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console());

        services.AddScoped<INotificationSender, NotificationSender>();
    })
    .AddMessaging();

builder.Build().Run();
