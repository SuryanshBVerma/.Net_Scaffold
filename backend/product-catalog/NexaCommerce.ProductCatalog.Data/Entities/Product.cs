namespace NexaCommerce.ProductCatalog.Data.Entities;

/// <summary>
/// A product in the catalog.
///
/// LEARNING — Entity design principles:
///   • Strongly-typed Id (Guid) — avoids accidental int/long mix-ups across services.
///   • Nullable ImageKey — not every product has an image yet; C# nullable forces you
///     to handle the "no image" case explicitly rather than checking for empty string.
///   • CreatedAt / UpdatedAt — audit trail without a separate audit table.
///   • Navigation property Category — EF Core uses this to generate a SQL JOIN.
///     No foreign key column needed in the C# class (EF infers CategoryId automatically).
/// </summary>
public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Price in the system's base currency (stored as decimal for precision).</summary>
    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The MinIO/S3 object key for the hero image.
    /// Null until an image is uploaded.
    /// Format: "products/{id}/hero.webp"
    /// </summary>
    public string? ImageKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // ── Navigation properties ────────────────────────────────────────────
    // LEARNING: Navigation properties let you write LINQ like:
    //   db.Products.Include(p => p.Category)
    // EF Core translates this to a SQL JOIN — no manual join syntax needed.

    public Guid CategoryId { get; set; }

    /// <summary>
    /// Navigation property: the category this product belongs to.
    /// EF Core will populate this when you call .Include(p => p.Category).
    /// </summary>
    public Category Category { get; set; } = null!;
}
