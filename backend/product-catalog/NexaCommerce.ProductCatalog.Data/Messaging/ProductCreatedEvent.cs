namespace NexaCommerce.ProductCatalog.Messaging;

/// <summary>
/// Published by ProductService after a product is successfully created.
///
/// LEARNING — Message contracts as shared facts:
///   This record is the ONLY thing the Notifications service needs to know
///   about ProductCatalog. It doesn't reference the Product entity, the
///   DbContext, or any ProductCatalog internals.
///
///   In Phase 5, the Notifications worker will have a class:
///     public class ProductCreatedHandler
///     {
///         public Task Handle(ProductCreatedEvent evt) { ... }
///     }
///   Wolverine discovers this handler by convention — no registration needed.
///
/// LEARNING — Record types for messages:
///   Records are immutable by default. Messages should never be mutated after
///   publication — use records to enforce this at the type system level.
///   Records also get value equality for free, which helps in tests.
/// </summary>
public sealed record ProductCreatedEvent(
    Guid ProductId,
    string ProductName,
    decimal Price,
    string CategoryName,
    DateTimeOffset CreatedAt);
