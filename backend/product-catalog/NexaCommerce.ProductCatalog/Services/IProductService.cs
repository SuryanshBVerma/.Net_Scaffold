using Ardalis.Result;
using NexaCommerce.ProductCatalog.Data.Entities;

namespace NexaCommerce.ProductCatalog.Services;

// ── Request / Response models ────────────────────────────────────────────────
// LEARNING: These are NOT the HTTP request/response DTOs (those live in Endpoints/).
// These are the service-layer contracts — what the service accepts and returns.
// This separation means you can call the service from tests, jobs, or gRPC
// without coupling to FastEndpoints types.

public sealed record ListProductsRequest(
    string? Category = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    int Page = 1,
    int PageSize = 20);

public sealed record ProductSummary(
    Guid Id,
    string Name,
    decimal Price,
    string CategoryName,
    string? ImageUrl);

public sealed record ProductDetail(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    bool IsActive,
    string CategoryName,
    string? ImageUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateProductRequest(
    string Name,
    string Description,
    decimal Price,
    Guid CategoryId);

public sealed record UpdateProductRequest(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    bool IsActive,
    Guid CategoryId);

public sealed record ProductPage<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

public sealed record CategoryStats(string CategoryName, int ProductCount, decimal AveragePrice);

// ── Interface ────────────────────────────────────────────────────────────────

/// <summary>
/// Product business logic surface.
///
/// LEARNING — Result[T] instead of exceptions:
///   Methods return Result[T] (from Ardalis.Result) instead of throwing.
///   Result.Success(value)  → HTTP 200
///   Result.NotFound()      → HTTP 404
///   Result.Invalid(errors) → HTTP 422
///   Result.Error(msg)      → HTTP 500
///
///   In FastEndpoints, endpoint.SendResult(result) maps these automatically.
///   No try/catch in endpoints. No exception middleware for business failures.
/// </summary>
public interface IProductService
{
    Task<Result<ProductPage<ProductSummary>>> ListAsync(ListProductsRequest request, CancellationToken ct = default);
    Task<Result<ProductDetail>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Result<ProductDetail>> CreateAsync(CreateProductRequest request, CancellationToken ct = default);
    Task<Result<ProductDetail>> UpdateAsync(UpdateProductRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Result<string>> UploadImageAsync(Guid productId, Stream imageStream, string contentType, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CategoryStats>>> GetCategoryStatsAsync(CancellationToken ct = default);
}
