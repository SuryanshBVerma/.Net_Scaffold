namespace NexaCommerce.ProductCatalog.Data.Entities;

/// <summary>
/// A product category (Electronics, Apparel, etc.).
///
/// LEARNING — Why a separate Category entity?
///   Storing category as a plain string on Product is tempting but fragile:
///   typos create phantom categories, renaming a category requires updating
///   every product row, and you can't query "all categories" efficiently.
///
///   With a Category entity + foreign key:
///   - LINQ GroupBy on Category.Name produces a clean aggregate query
///   - Renaming a category is a single UPDATE on the categories table
///   - Product → Category is a navigation property → EF generates the JOIN
/// </summary>
public class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // LEARNING: Inverse navigation — the list of all products in this category.
    // You only need this if you want to write:
    //   db.Categories.Include(c => c.Products)
    // Otherwise you can omit it and navigate the other way (Product → Category).
    public ICollection<Product> Products { get; set; } = [];
}
