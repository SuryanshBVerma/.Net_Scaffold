namespace NexaCommerce.SharedKernel.Middleware;

/// <summary>
/// Ensures every HTTP request has an X-Correlation-Id header and that the
/// same ID appears in every log line produced during that request.
///
/// LEARNING: In a microservices system, a single user action (e.g. "add to cart")
/// may trigger calls across 3–5 services. The Correlation ID is the thread that
/// ties all those separate log entries together. Without it you are blind.
///
/// Behaviour:
///   1. If the incoming request already has X-Correlation-Id (set by the client
///      or an upstream proxy like Traefik) → use it.
///   2. If not → generate a new GUID.
///   3. Add the ID to the response headers so the caller can log it too.
///   4. Enrich the Serilog log context so every Log.* call within this request
///      automatically includes CorrelationId as a structured property.
///
/// Contract with Traefik:
///   Traefik can be configured to forward X-Correlation-Id (or generate one
///   via the requestid middleware). This middleware reads whatever Traefik sets.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        // 1. Read from incoming header or generate a new short ID.
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N")[..12]; // Short 12-char hex for readability.

        // 2. Store on HttpContext.Items so any code in the pipeline can read it.
        context.Items["CorrelationId"] = correlationId;

        // 3. Echo back on the response so the caller can correlate their own logs.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // 4. Enrich the Serilog LogContext for this request scope.
        //    Every logger.LogInformation(...) call within this request will now
        //    automatically include { "CorrelationId": "abc123def456" } in JSON output.
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
