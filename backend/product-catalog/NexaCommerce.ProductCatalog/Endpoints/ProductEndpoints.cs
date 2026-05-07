using Ardalis.Result;
using FastEndpoints;
using NexaCommerce.ProductCatalog.Services;
using NexaCommerce.SharedKernel.Auth;

namespace NexaCommerce.ProductCatalog.Endpoints;

// ═══════════════════════════════════════════════════════════════════════════════
// LEARNING — REPR Pattern (Request → Endpoint → Response)
//
// Each HTTP operation is ONE class. No shared controllers.
//
// Benefits vs. Controllers:
//   • Single Responsibility — one class does one thing
//   • Easy to find: "where is DELETE /api/products/{id}?" → DeleteProductEndpoint
//   • No [ApiController] attribute sprinkling or action filter mismatches
//   • Pre-processors, post-processors, and throttling are per-endpoint
//
// Structure of each endpoint class:
//   Request  — the model bound from query/body/route
//   Response — the model written to the HTTP response
//   Configure() — declares the route, HTTP verb, auth, and metadata
//   HandleAsync() — the handler; calls the service; maps to response
// ═══════════════════════════════════════════════════════════════════════════════


// ── GET /api/products ────────────────────────────────────────────────────────

public sealed class ListProductsRequest
{
    public string? Category { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class ListProductsEndpoint(IProductService products)
    : Endpoint<ListProductsRequest, ProductPage<ProductSummary>>
{
    public override void Configure()
    {
        Get("/api/products");
        AllowAnonymous();   // Public catalogue — no auth needed to browse
        Summary(s =>
        {
            s.Summary = "List products with optional filtering and pagination";
            s.Description = "Returns a paged list of active products. Filter by category and price range.";
        });
    }

    public override async Task HandleAsync(ListProductsRequest req, CancellationToken ct)
    {
        var result = await products.ListAsync(
            new Services.ListProductsRequest(req.Category, req.MinPrice, req.MaxPrice, req.Page, req.PageSize), ct);

        await this.SendMappedResultAsync(result, ct);
    }
}


// ── GET /api/products/{id} ───────────────────────────────────────────────────

public sealed class GetProductRequest { public Guid Id { get; init; } }

public sealed class GetProductEndpoint(IProductService products)
    : Endpoint<GetProductRequest, ProductDetail>
{
    public override void Configure()
    {
        Get("/api/products/{id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetProductRequest req, CancellationToken ct)
    {
        var result = await products.GetByIdAsync(req.Id, ct);
        await this.SendMappedResultAsync(result, ct);
    }
}


// ── POST /api/products ───────────────────────────────────────────────────────

public sealed class CreateProductHttpRequest
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public Guid CategoryId { get; init; }
}

[RequirePermission("products:write")]
public sealed class CreateProductEndpoint(IProductService products)
    : Endpoint<CreateProductHttpRequest, ProductDetail>
{
    public override void Configure()
    {
        Post("/api/products");
        // LEARNING: [RequirePermission] on the class is read by PermissionPreProcessor.
        // The pre-processor runs before HandleAsync() and returns 403 if the user
        // lacks "products:write". No code in HandleAsync() checks permissions.
        Description(d => d.WithSummary("Create a new product. Requires products:write permission."));
    }

    public override async Task HandleAsync(CreateProductHttpRequest req, CancellationToken ct)
    {
        var result = await products.CreateAsync(
            new CreateProductRequest(req.Name, req.Description, req.Price, req.CategoryId), ct);

        await this.SendMappedResultAsync(result, ct, successCode: 201);
    }
}


// ── PUT /api/products/{id} ───────────────────────────────────────────────────

public sealed class UpdateProductHttpRequest
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public bool IsActive { get; init; }
    public Guid CategoryId { get; init; }
}

[RequirePermission("products:write")]
public sealed class UpdateProductEndpoint(IProductService products)
    : Endpoint<UpdateProductHttpRequest, ProductDetail>
{
    public override void Configure()
    {
        Put("/api/products/{id}");
    }

    public override async Task HandleAsync(UpdateProductHttpRequest req, CancellationToken ct)
    {
        var result = await products.UpdateAsync(
            new UpdateProductRequest(req.Id, req.Name, req.Description, req.Price, req.IsActive, req.CategoryId), ct);
        await this.SendMappedResultAsync(result, ct);
    }
}


// ── DELETE /api/products/{id} ────────────────────────────────────────────────

public sealed class DeleteProductRequest { public Guid Id { get; init; } }

[RequirePermission("products:delete")]
public sealed class DeleteProductEndpoint(IProductService products) : Endpoint<DeleteProductRequest>
{
    public override void Configure()
    {
        Delete("/api/products/{id}");
    }

    public override async Task HandleAsync(DeleteProductRequest req, CancellationToken ct)
    {
        var result = await products.DeleteAsync(req.Id, ct);
        await this.SendMappedResultAsync(result, ct, successCode: 204);
    }
}


// ── POST /api/products/{id}/image ────────────────────────────────────────────
// File upload endpoint — reads from IFormFile, streams to MinIO.

public sealed class UploadProductImageRequest
{
    public Guid Id { get; init; }
    public IFormFile? Image { get; init; }
}

public sealed class UploadProductImageResponse { public string ImageUrl { get; init; } = string.Empty; }

[RequirePermission("products:write")]
public sealed class UploadProductImageEndpoint(IProductService products)
    : Endpoint<UploadProductImageRequest, UploadProductImageResponse>
{
    public override void Configure()
    {
        Post("/api/products/{id}/image");
        AllowFileUploads();   // Enables multipart/form-data parsing
        Description(d => d.WithSummary("Upload hero image for a product. Stored in MinIO (S3-compatible)."));
    }

    public override async Task HandleAsync(UploadProductImageRequest req, CancellationToken ct)
    {
        if (req.Image is null || req.Image.Length == 0)
        {
            HttpContext.Response.StatusCode = 400;
            await HttpContext.Response.WriteAsJsonAsync(new { error = "No image file provided." }, ct);
            return;
        }

        await using var stream = req.Image.OpenReadStream();

        var result = await products.UploadImageAsync(req.Id, stream, req.Image.ContentType, ct);

        if (result.IsSuccess)
            await HttpContext.Response.SendAsync(
                new UploadProductImageResponse { ImageUrl = result.Value }, 200, cancellation: ct);
        else
            await this.SendMappedResultAsync(result, ct);
    }
}


// ── GET /api/products/stats ──────────────────────────────────────────────────
// Demonstrates the GROUP BY LINQ query.

public sealed class GetCategoryStatsEndpoint(IProductService products)
    : EndpointWithoutRequest<IReadOnlyList<CategoryStats>>
{
    public override void Configure()
    {
        Get("/api/products/stats");
        AllowAnonymous();
        Description(d => d.WithSummary("Product counts and average price per category (GROUP BY demo)."));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await products.GetCategoryStatsAsync(ct);
        await this.SendMappedResultAsync(result, ct);
    }
}
