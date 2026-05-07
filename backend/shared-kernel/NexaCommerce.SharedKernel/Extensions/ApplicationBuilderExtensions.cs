using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using NexaCommerce.SharedKernel.Auth;
using NexaCommerce.SharedKernel.Endpoints;
using NexaCommerce.SharedKernel.Middleware;

namespace NexaCommerce.SharedKernel.Extensions;

/// <summary>
/// Extension methods for IApplicationBuilder / WebApplication — the middleware pipeline layer.
///
/// LEARNING: The order of middleware in ASP.NET Core is critical.
/// UseRouting → UseAuthentication → UseAuthorization → your middleware → endpoints.
/// This method enforces the correct order so each service doesn't have to get it right.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the standard NexaCommerce middleware pipeline in the correct order:
    ///   1. CorrelationId — must be first so all subsequent logs have the ID
    ///   2. Authentication — validates the JWT
    ///   3. Authorization — evaluates policies
    ///   4. UserContext — reads the now-validated claims into IUserContext
    ///   5. FastEndpoints — maps all endpoint routes
    ///   6. Health check endpoint at /health
    ///   7. Swagger UI at /swagger
    ///
    /// LEARNING: MapHealthChecks uses ResponseWriter from HealthChecks.UI.Client
    /// so the JSON format is compatible with Kubernetes probes AND the Aspire dashboard.
    /// </summary>
    public static WebApplication UseNexaCommerceDefaults(this WebApplication app)
    {
        // Must be first — every log line in the pipeline will have the correlation ID.
        app.UseMiddleware<CorrelationIdMiddleware>();

        app.UseAuthentication();
        app.UseAuthorization();

        // Populate IUserContext from the now-validated ClaimsPrincipal.
        app.UseMiddleware<UserContextMiddleware>();

        // FastEndpoints routes all [Endpoint] classes and runs their pre-processors.
        app.UseFastEndpoints(cfg =>
        {
            cfg.Endpoints.Configurator = ep =>
            {
                // Register PermissionPreProcessor globally for all endpoints.
                ep.PreProcessors(Order.Before, new PermissionPreProcessor());
            };
        });

        // Swagger UI — accessible at /swagger in development.
        app.UseSwaggerGen();

        // Health check at /health — returns 200 OK when all checks pass.
        // LEARNING: In Kubernetes, the liveness probe calls /health.
        //           Aspire dashboard polls /health to show service state.
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = HealthChecks.UI.Client.UIResponseWriter.WriteHealthCheckUIResponse
        });

        return app;
    }

    /// <summary>
    /// Runs EF Core migrations at startup — idempotent, safe to call on every boot.
    ///
    /// LEARNING: In production, prefer running migrations in a dedicated init
    /// container (Kubernetes) or CI step. In development and this scaffold,
    /// auto-migration on startup is fine and simplifies the learning loop.
    ///
    /// The Aspire AppHost calls WaitFor(dbResource) before starting the service,
    /// so the database is guaranteed to be ready when this runs.
    /// </summary>
    public static async Task MigrateDatabase<TDbContext>(this WebApplication app)
        where TDbContext : DbContext
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TDbContext>>();

        logger.LogInformation("Running EF Core migrations for {DbContext}...", typeof(TDbContext).Name);

        // MigrateAsync applies any pending migrations and is a no-op if up-to-date.
        await db.Database.MigrateAsync();

        logger.LogInformation("Migrations complete for {DbContext}", typeof(TDbContext).Name);
    }
}
