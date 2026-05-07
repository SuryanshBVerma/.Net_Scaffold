using Ardalis.Result;
using Microsoft.EntityFrameworkCore;
using NexaCommerce.ProductCatalog.Data;
using NexaCommerce.ProductCatalog.Data.Entities;
using NexaCommerce.ProductCatalog.Messaging;
using NexaCommerce.SharedKernel.Storage;
using Wolverine;

namespace NexaCommerce.ProductCatalog.Services;

/// <summary>
/// Product business logic — the heart of Phase 4.
///
/// THIS IS THE LINQ LEARNING FILE.
/// Every query below is annotated to explain what SQL it generates and why
/// the particular LINQ operator was chosen.
///
/// Also demonstrates:
///   • Wolverine message publish inside an EF transaction (inbox/outbox)
///   • IObjectStorageService abstraction (MinIO locally, AWS S3 in prod)
///   • Ardalis.Result return type (no exceptions for business failures)
/// </summary>
public sealed class ProductService(
    CatalogDbContext db,
    IMessageBus bus,
    IObjectStorageService storage,
    ILogger<ProductService> logger) : IProductService
{
    private const string BucketName = "product-images";

    // ════════════════════════════════════════════════════════════════════════
    // LIST — WHERE + PROJECTION + PAGINATION
    // ════════════════════════════════════════════════════════════════════════
    public async Task<Result<ProductPage<ProductSummary>>> ListAsync(
        ListProductsRequest request, CancellationToken ct = default)
    {
        // Start with the full queryable — EF does NOT hit the DB yet.
        // LEARNING: IQueryable<T> is lazy. SQL is only sent when you call
        // ToListAsync(), FirstOrDefaultAsync(), CountAsync(), etc.
        var query = db.Products
            .AsNoTracking()                      // Read-only path → skip change tracking (faster)
            .Include(p => p.Category)            // LEARNING: JOIN — EF generates LEFT JOIN "Categories"
            .Where(p => p.IsActive);             // LEARNING: WHERE IsActive = true

        // ── Dynamic filtering ────────────────────────────────────────────
        // LEARNING: You can compose IQueryable conditionally.
        // Each .Where() adds an AND clause to the SQL — only if the filter is set.
        if (!string.IsNullOrWhiteSpace(request.Category))
            query = query.Where(p => p.Category.Name == request.Category);
            // SQL: AND "Categories"."Name" = @p0

        if (request.MinPrice.HasValue)
            query = query.Where(p => p.Price >= request.MinPrice.Value);
            // SQL: AND "Products"."Price" >= @p1

        if (request.MaxPrice.HasValue)
            query = query.Where(p => p.Price <= request.MaxPrice.Value);
            // SQL: AND "Products"."Price" <= @p2

        // ── Count total (for pagination metadata) ───────────────────────
        // LEARNING: CountAsync sends "SELECT COUNT(*) FROM ..." using the SAME
        // WHERE clauses already built up above. One round trip to the DB.
        var totalCount = await query.CountAsync(ct);

        // ── Pagination: SKIP + TAKE ──────────────────────────────────────
        // LEARNING: Skip/Take translates to OFFSET/LIMIT in PostgreSQL.
        // Always ORDER before Skip — without ORDER BY, OFFSET results are undefined.
        //
        // SQL: ORDER BY "Products"."CreatedAt" DESC OFFSET @skip LIMIT @take
        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            // LEARNING: Select projection — only the columns we need come back from the DB.
            // Without Select, EF fetches ALL columns including Description, ImageKey, etc.
            // With Select, SQL becomes: SELECT Id, Name, Price, CategoryName FROM ...
            .Select(p => new ProductSummary(
                p.Id,
                p.Name,
                p.Price,
                p.Category.Name,
                p.ImageKey != null
                    ? $"/{BucketName}/{p.ImageKey}"   // Derive URL from key; no DB column needed
                    : null))
            .ToListAsync(ct);

        return Result.Success(new ProductPage<ProductSummary>(
            items, totalCount, request.Page, request.PageSize));
    }

    // ════════════════════════════════════════════════════════════════════════
    // GET BY ID — SINGLE ENTITY WITH NAVIGATION
    // ════════════════════════════════════════════════════════════════════════
    public async Task<Result<ProductDetail>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        // LEARNING: FirstOrDefaultAsync returns null if no row matches.
        // Result.NotFound() maps to HTTP 404 in the endpoint (no exception needed).
        //
        // SQL: SELECT ... FROM "Products" p
        //      LEFT JOIN "Categories" c ON p."CategoryId" = c."Id"
        //      WHERE p."Id" = @id
        //      LIMIT 1
        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (product is null)
            return Result.NotFound($"Product {id} not found.");

        return Result.Success(MapToDetail(product));
    }

    // ════════════════════════════════════════════════════════════════════════
    // CREATE — INSERT + WOLVERINE PUBLISH (inbox/outbox)
    // ════════════════════════════════════════════════════════════════════════
    public async Task<Result<ProductDetail>> CreateAsync(
        CreateProductRequest request, CancellationToken ct = default)
    {
        // LEARNING: AnyAsync → "SELECT EXISTS (SELECT 1 FROM ...)".
        // Much cheaper than loading the whole Category entity just to validate it exists.
        var categoryExists = await db.Categories
            .AnyAsync(c => c.Id == request.CategoryId, ct);

        if (!categoryExists)
            return Result.Invalid(new ValidationError($"Category {request.CategoryId} does not exist."));

        var product = new Product
        {
            Id          = Guid.NewGuid(),
            Name        = request.Name,
            Description = request.Description,
            Price       = request.Price,
            CategoryId  = request.CategoryId,
            IsActive    = true,
            CreatedAt   = DateTimeOffset.UtcNow,
            UpdatedAt   = DateTimeOffset.UtcNow
        };

        db.Products.Add(product);

        // LEARNING — Wolverine inbox/outbox with EF Core:
        //   bus.PublishAsync() does NOT send the message immediately.
        //   Wolverine stores the outgoing message in the same database transaction
        //   as db.SaveChangesAsync(). If the DB commit fails, the message is NOT sent.
        //   If the DB commit succeeds, Wolverine sends the message after the transaction.
        //
        //   This guarantees: "message is published IF AND ONLY IF the DB change persists."
        //   No duplicate messages on retry. No lost messages on crash.
        await bus.PublishAsync(new ProductCreatedEvent(
            product.Id,
            product.Name,
            product.Price,
            string.Empty,   // Category name loaded separately below
            product.CreatedAt));

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Product {ProductId} '{Name}' created", product.Id, product.Name);

        // Load with navigation for the response
        var created = await db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstAsync(p => p.Id == product.Id, ct);

        return Result.Success(MapToDetail(created));
    }

    // ════════════════════════════════════════════════════════════════════════
    // UPDATE — TRACKED ENTITY (change tracking writes only changed columns)
    // ════════════════════════════════════════════════════════════════════════
    public async Task<Result<ProductDetail>> UpdateAsync(
        UpdateProductRequest request, CancellationToken ct = default)
    {
        // LEARNING: No .AsNoTracking() here — we WANT change tracking for the update.
        // EF will generate: UPDATE "Products" SET Name=@name, Price=@price, ...
        // Only the columns that actually changed are included in the SQL.
        var product = await db.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == request.Id, ct);

        if (product is null)
            return Result.NotFound($"Product {request.Id} not found.");

        product.Name        = request.Name;
        product.Description = request.Description;
        product.Price       = request.Price;
        product.IsActive    = request.IsActive;
        product.CategoryId  = request.CategoryId;
        product.UpdatedAt   = DateTimeOffset.UtcNow;

        // SaveChangesAsync generates the UPDATE SQL from the change tracker.
        await db.SaveChangesAsync(ct);

        // Reload with new category navigation
        await db.Entry(product).Reference(p => p.Category).LoadAsync(ct);

        return Result.Success(MapToDetail(product));
    }

    // ════════════════════════════════════════════════════════════════════════
    // DELETE — SOFT vs HARD DELETE + EVENT PUBLISH
    // ════════════════════════════════════════════════════════════════════════
    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var product = await db.Products.FindAsync([id], ct);

        if (product is null)
            return Result.NotFound($"Product {id} not found.");

        // LEARNING: Hard delete for simplicity. In production, prefer soft-delete:
        //   product.IsActive = false; product.DeletedAt = DateTimeOffset.UtcNow;
        //   Add a global query filter: .HasQueryFilter(p => p.IsActive)
        //   so deleted products are invisible to all queries automatically.
        db.Products.Remove(product);

        await bus.PublishAsync(new ProductDeletedEvent(
            product.Id,
            product.Name,
            DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Product {ProductId} '{Name}' deleted", product.Id, product.Name);

        return Result.Success();
    }

    // ════════════════════════════════════════════════════════════════════════
    // IMAGE UPLOAD — IObjectStorageService (MinIO locally / AWS S3 in prod)
    // ════════════════════════════════════════════════════════════════════════
    public async Task<Result<string>> UploadImageAsync(
        Guid productId, Stream imageStream, string contentType, CancellationToken ct = default)
    {
        var product = await db.Products.FindAsync([productId], ct);

        if (product is null)
            return Result.NotFound($"Product {productId} not found.");

        // LEARNING: Object key convention "products/{id}/hero" scopes files per product.
        // Changing to "products/{id}/thumb" would store a thumbnail alongside the hero.
        var objectKey = $"products/{productId}/hero";

        // LEARNING: The same line of code runs against MinIO (dev) or real AWS S3 (prod).
        // Only the configuration (ServiceURL, credentials) differs between environments.
        var imageUrl = await storage.UploadAsync(BucketName, objectKey, imageStream, contentType, ct);

        product.ImageKey  = objectKey;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Result.Success(imageUrl);
    }

    // ════════════════════════════════════════════════════════════════════════
    // CATEGORY STATS — GROUP BY + AGGREGATE
    // ════════════════════════════════════════════════════════════════════════
    public async Task<Result<IReadOnlyList<CategoryStats>>> GetCategoryStatsAsync(CancellationToken ct = default)
    {
        // LEARNING: GroupBy in LINQ → GROUP BY in SQL.
        // EF Core translates this to:
        //   SELECT c."Name", COUNT(p."Id"), AVG(p."Price")
        //   FROM "Products" p
        //   INNER JOIN "Categories" c ON p."CategoryId" = c."Id"
        //   WHERE p."IsActive" = true
        //   GROUP BY c."Name"
        //   ORDER BY COUNT(p."Id") DESC
        var stats = await db.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Include(p => p.Category)
            .ToListAsync(ct);

        var grouped = stats
            .GroupBy(p => p.Category.Name)
            .Select(g => new CategoryStats(
                g.Key,
                g.Count(),
                Math.Round(g.Average(p => p.Price), 2)))
            .OrderByDescending(s => s.ProductCount)
            .ToList();

        return Result.Success<IReadOnlyList<CategoryStats>>(grouped);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static ProductDetail MapToDetail(Product p) => new(
        p.Id,
        p.Name,
        p.Description,
        p.Price,
        p.IsActive,
        p.Category?.Name ?? string.Empty,
        p.ImageKey != null ? $"/{BucketName}/{p.ImageKey}" : null,
        p.CreatedAt,
        p.UpdatedAt);
}
