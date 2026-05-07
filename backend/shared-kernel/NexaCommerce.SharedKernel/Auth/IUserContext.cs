namespace NexaCommerce.SharedKernel.Auth;

/// <summary>
/// Typed representation of the current authenticated user.
///
/// LEARNING: Instead of injecting IHttpContextAccessor and reading
/// HttpContext.User.FindFirst(ClaimTypes.NameIdentifier) everywhere,
/// services inject IUserContext and call .UserId, .Roles, etc.
///
/// Benefits:
///   1. No magic claim-type strings scattered across the codebase.
///   2. Easily mockable in unit tests — just mock IUserContext.
///   3. Works in background workers that have no HttpContext at all
///      (populate a different implementation from the message envelope).
/// </summary>
public interface IUserContext
{
    /// <summary>The unique user identifier from the JWT "sub" claim.</summary>
    string UserId { get; }

    /// <summary>The user's display name from the JWT "name" claim.</summary>
    string Name { get; }

    /// <summary>Email address from the JWT "email" claim.</summary>
    string Email { get; }

    /// <summary>Roles assigned to the user (from the JWT "roles" claim array).</summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>Fine-grained permission strings (e.g. "products:write", "orders:read").</summary>
    IReadOnlyList<string> Permissions { get; }

    /// <summary>Whether the user has been successfully authenticated.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Check if the user holds a specific permission.</summary>
    bool HasPermission(string permission);
}
