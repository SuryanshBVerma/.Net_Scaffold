using NexaCommerce.SharedKernel.Auth;

namespace NexaCommerce.SharedKernel.Auth;

/// <summary>
/// Middleware that runs after JWT validation and populates IUserContext
/// for the current request scope.
///
/// LEARNING: Middleware order matters in ASP.NET Core.
/// UseAuthentication() → UseAuthorization() → UseNexaCommerceDefaults()
///        (validates JWT)    (builds policy)    (this middleware runs here)
///
/// By the time this middleware runs, HttpContext.User is already populated
/// with validated claims. We simply map them to our typed IUserContext.
///
/// Claim type mapping:
///   "sub"         → UserId       (OIDC standard subject identifier)
///   "name"        → Name
///   "email"       → Email
///   "roles"       → Roles        (array claim, may appear multiple times)
///   "permissions" → Permissions  (custom claim added by your auth server)
/// </summary>
public sealed class UserContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, IUserContext userContext)
    {
        // IUserContext is Scoped — we get the concrete UserContext impl here.
        // This is safe because we registered UserContext as its own Scoped type
        // and IUserContext resolves to the same instance within the request.
        if (userContext is UserContext ctx && httpContext.User.Identity?.IsAuthenticated == true)
        {
            ctx.IsAuthenticated = true;
            ctx.UserId = httpContext.User.FindFirstValue("sub")
                      ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? string.Empty;
            ctx.Name  = httpContext.User.FindFirstValue("name")
                     ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
                     ?? string.Empty;
            ctx.Email = httpContext.User.FindFirstValue("email")
                     ?? httpContext.User.FindFirstValue(ClaimTypes.Email)
                     ?? string.Empty;

            // "roles" may appear as multiple claims — collect all of them.
            ctx.Roles = httpContext.User
                .FindAll("roles")
                .Concat(httpContext.User.FindAll(ClaimTypes.Role))
                .Select(c => c.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Fine-grained permissions (custom claim from your auth server).
            ctx.Permissions = httpContext.User
                .FindAll("permissions")
                .Select(c => c.Value)
                .ToList();

            // Enrich the log scope so every log line in this request includes
            // UserId and CorrelationId automatically.
            using var logScope = httpContext.RequestServices
                .GetRequiredService<ILogger<UserContextMiddleware>>()
                .BeginScope(new Dictionary<string, object>
                {
                    ["UserId"] = ctx.UserId,
                    ["UserName"] = ctx.Name
                });

            await next(httpContext);
            return;
        }

        await next(httpContext);
    }
}
