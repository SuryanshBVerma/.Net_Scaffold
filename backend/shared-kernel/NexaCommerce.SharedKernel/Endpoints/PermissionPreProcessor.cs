using FastEndpoints;
using NexaCommerce.SharedKernel.Auth;

namespace NexaCommerce.SharedKernel.Endpoints;

/// <summary>
/// FastEndpoints GLOBAL pre-processor that enforces [RequirePermission] attributes.
///
/// LEARNING: IGlobalPreProcessor runs before the Handle() method of EVERY endpoint.
/// It inspects the endpoint metadata to find [RequirePermission] and short-circuits
/// the request if the user lacks the required permission.
///
/// Flow:
///   Request arrives
///     → JWT validated (UseAuthentication)
///     → IUserContext populated (UserContextMiddleware)
///     → PermissionPreProcessor runs  ← HERE (globally, on every endpoint)
///         [RequirePermission] not present → pass through
///         user not authenticated         → 401 Unauthorized
///         user lacks permission          → 403 Forbidden
///         user has permission            → Handle() is called
///
/// Why not ASP.NET Core policies?
///   Policies require registration in DI by name. For 20+ fine-grained
///   permissions that becomes verbose boilerplate. This approach scales
///   to any permission string without registration.
/// </summary>
public sealed class PermissionPreProcessor : IGlobalPreProcessor
{
    public async Task PreProcessAsync(IPreProcessorContext ctx, CancellationToken ct)
    {
        // Check if the endpoint class is decorated with [RequirePermission].
        var attr = ctx.HttpContext.GetEndpoint()
            ?.Metadata
            .GetMetadata<RequirePermissionAttribute>();

        // No attribute → no permission required → continue.
        if (attr is null) return;

        var userContext = ctx.HttpContext.RequestServices.GetRequiredService<IUserContext>();

        if (!userContext.IsAuthenticated)
        {
            // Not authenticated at all → 401 Unauthorized.
            await ctx.HttpContext.Response.SendUnauthorizedAsync(ct);
            return;
        }

        if (!userContext.HasPermission(attr.Permission))
        {
            // Authenticated but lacks the required permission → 403 Forbidden.
            // LEARNING: 401 = "who are you?" / 403 = "I know who you are, but no."
            await ctx.HttpContext.Response.SendForbiddenAsync(ct);
        }
    }
}
