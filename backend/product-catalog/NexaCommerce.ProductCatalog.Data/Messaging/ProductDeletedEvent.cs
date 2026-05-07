namespace NexaCommerce.ProductCatalog.Messaging;

/// <summary>
/// Published by ProductService after a product is deleted.
///
/// LEARNING — Why publish an event on delete?
///   The Notifications service may want to alert downstream systems (e.g. an
///   e-commerce front-end cache, a search index, or a warehouse system) that
///   a product is no longer available. Without this event, those systems would
///   be stale until their next full sync.
///
///   This is the "event-carried state transfer" pattern: the event carries
///   enough data for consumers to act without querying ProductCatalog again.
/// </summary>
public sealed record ProductDeletedEvent(
    Guid ProductId,
    string ProductName,
    DateTimeOffset DeletedAt);
