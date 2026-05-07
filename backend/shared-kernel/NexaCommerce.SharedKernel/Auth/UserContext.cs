namespace NexaCommerce.SharedKernel.Auth;

/// <summary>
/// Scoped implementation of IUserContext populated by UserContextMiddleware.
///
/// LEARNING: This is registered as Scoped (one instance per HTTP request).
/// UserContextMiddleware fills it from the validated ClaimsPrincipal once,
/// and then every service within that request gets the same pre-parsed object.
///
/// The "internal set" properties keep mutation within this assembly —
/// only UserContextMiddleware can write to them.
/// </summary>
internal sealed class UserContext : IUserContext
{
    public string UserId { get; internal set; } = string.Empty;
    public string Name { get; internal set; } = string.Empty;
    public string Email { get; internal set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; internal set; } = [];
    public IReadOnlyList<string> Permissions { get; internal set; } = [];
    public bool IsAuthenticated { get; internal set; }

    public bool HasPermission(string permission) =>
        Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}
