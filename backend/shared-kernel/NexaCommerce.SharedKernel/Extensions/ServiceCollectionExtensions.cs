using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using NexaCommerce.SharedKernel.Auth;
using NexaCommerce.SharedKernel.Endpoints;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace NexaCommerce.SharedKernel.Extensions;

/// <summary>
/// Extension methods for IServiceCollection — the DI registration layer.
///
/// LEARNING: Extension methods on IServiceCollection are the idiomatic .NET way
/// to package cross-cutting infrastructure setup. By calling AddNexaCommerceDefaults()
/// in a 2-line Program.cs, every service gets logging, OTEL, and health checks.
/// Individual choices (auth, FastEndpoints) are separated into focused methods.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything all NexaCommerce services need:
    ///   • Serilog structured logging (reads from configuration)
    ///   • OpenTelemetry traces + metrics (Aspire injects OTLP endpoint)
    ///   • Health checks endpoint (/health)
    ///   • IUserContext (scoped, populated per-request by middleware)
    ///
    /// LEARNING: A "defaults" method like this enforces consistency. Services
    /// can't "forget" to add OTEL or health checks — it's included by default.
    /// To override, call the individual methods and skip this one.
    /// </summary>
    public static IServiceCollection AddNexaCommerceDefaults(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        // ── Serilog ─────────────────────────────────────────────────────────
        // LEARNING: Serilog reads its configuration from appsettings.json
        // (MinimumLevel, Sinks, Enrich). The UseSerilog() call in Program.cs
        // replaces the default Microsoft.Extensions.Logging providers.
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()          // Picks up LogContext.PushProperty() values
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} {CorrelationId} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        services.AddLogging(lb => lb.ClearProviders().AddSerilog(dispose: true));

        // ── OpenTelemetry ────────────────────────────────────────────────────
        // LEARNING: Aspire auto-injects OTEL_EXPORTER_OTLP_ENDPOINT into every
        // AddProject<> resource. AddOtlpExporter() reads that env var, so traces
        // automatically appear in the Aspire dashboard with zero manual config.
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()  // Traces every HTTP request
                .AddHttpClientInstrumentation()  // Traces outgoing HTTP calls
                .AddOtlpExporter())              // Sends to Aspire / Jaeger / Tempo
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter());

        // ── Health checks ────────────────────────────────────────────────────
        // LEARNING: /health is consumed by Kubernetes probes AND the Aspire
        // dashboard. AspNetCore.HealthChecks.UI.Client formats the JSON in the
        // standard schema both understand.
        services.AddHealthChecks();

        // ── Typed identity ───────────────────────────────────────────────────
        // LEARNING: Register UserContext as BOTH its concrete type AND as the
        // IUserContext interface — both pointing to the same Scoped instance.
        // UserContextMiddleware resolves UserContext (concrete) to write to it.
        // Business code resolves IUserContext (interface) to read from it.
        services.AddScoped<UserContext>();
        services.AddScoped<IUserContext>(sp => sp.GetRequiredService<UserContext>());

        return services;
    }

    /// <summary>
    /// Registers JWT Bearer authentication.
    ///
    /// LEARNING: Reads Authority and Audience from configuration.
    /// Authority is your OIDC provider (Keycloak, Auth0, Azure AD B2C, etc.).
    /// The middleware validates the JWT signature against the provider's JWKS endpoint.
    /// </summary>
    public static IServiceCollection AddNexaCommerceAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // The OIDC authority — validates token issuer and fetches signing keys.
                options.Authority = configuration["Auth:Authority"];

                // Audience must match the "aud" claim in the JWT.
                options.Audience = configuration["Auth:Audience"];

                options.TokenValidationParameters.ValidateAudience =
                    !string.IsNullOrEmpty(configuration["Auth:Audience"]);

                // In development with self-signed certs (e.g. Keycloak in Docker),
                // you may need to disable SSL validation temporarily:
                // options.BackchannelHttpHandler = new HttpClientHandler
                //     { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
            });

        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Registers FastEndpoints with Swagger/OpenAPI.
    ///
    /// LEARNING: FastEndpoints.AddFastEndpoints() scans all assemblies for
    /// endpoint classes (subclasses of Endpoint[TReq, TRes]). The
    /// GlobalPreProcessor registers PermissionPreProcessor for every endpoint.
    /// </summary>
    public static IServiceCollection AddNexaFastEndpoints(
        this IServiceCollection services)
    {
        services.AddFastEndpoints(options =>
        {
            // LEARNING: Global pre-processors run before EVERY endpoint's Handle().
            // PermissionPreProcessor reads [RequirePermission] from the endpoint class.
            options.Assemblies = [typeof(ServiceCollectionExtensions).Assembly];
        });

        services.SwaggerDocument(o =>
        {
            o.DocumentSettings = s =>
            {
                s.Title = "NexaCommerce API";
                s.Version = "v1";
            };
        });

        return services;
    }
}
