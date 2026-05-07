namespace NexaCommerce.SharedKernel.Auth;

/// <summary>
/// Marks a FastEndpoints endpoint as requiring a specific permission.
///
/// LEARNING: Instead of building ASP.NET Core policies for each permission,
/// we use a lightweight attribute read by PermissionPreProcessor.
///
/// Usage on an endpoint:
///   [RequirePermission("products:write")]
///   public class CreateProductEndpoint : Endpoint[CreateProductRequest, CreateProductResponse]
///
/// Convention: "resource:action"
///   products:read    — list or get a product
///   products:write   — create or update a product
///   products:delete  — delete a product
///   reports:read     — access generated reports
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequirePermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}
