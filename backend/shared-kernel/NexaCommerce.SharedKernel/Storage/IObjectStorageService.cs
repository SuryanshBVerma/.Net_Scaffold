namespace NexaCommerce.SharedKernel.Storage;

/// <summary>
/// Abstraction for S3-compatible object storage.
///
/// LEARNING — Hexagonal architecture (Ports &amp; Adapters):
///   This interface is the PORT — it defines what the application needs
///   from storage without caring how it is implemented.
///
///   Adapters (implementations):
///     • MinioObjectStorageService  — MinIO container running locally via Aspire
///     • (future) S3ObjectStorageService — AWS S3 in production
///
///   The ProductCatalog service injects IObjectStorageService. The only
///   change between environments is which adapter is registered in DI.
///   Zero business code changes. Zero test changes.
///
/// Naming convention for object keys: "{entity-type}/{id}/{filename}"
///   e.g. "products/abc123/hero-image.webp"
/// </summary>
public interface IObjectStorageService
{
    /// <summary>
    /// Uploads a file to the specified bucket.
    /// </summary>
    /// <param name="bucketName">Target bucket (created automatically if it doesn't exist).</param>
    /// <param name="objectKey">The storage path/key, e.g. "products/abc123/hero.webp".</param>
    /// <param name="content">File stream to upload.</param>
    /// <param name="contentType">MIME type, e.g. "image/webp".</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The public-accessible URL of the uploaded object.</returns>
    Task<string> UploadAsync(
        string bucketName,
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken ct = default);

    /// <summary>
    /// Downloads an object and returns its content as a stream.
    /// </summary>
    Task<Stream> DownloadAsync(
        string bucketName,
        string objectKey,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes an object. Does not throw if the object does not exist.
    /// </summary>
    Task DeleteAsync(
        string bucketName,
        string objectKey,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a pre-signed URL that allows temporary direct access to an object
    /// without credentials — useful for serving large files via CDN or browser.
    ///
    /// LEARNING: Pre-signed URLs are the recommended way to serve private S3 objects.
    /// The URL expires after the given duration. The client downloads directly from
    /// storage — your API is not in the data path.
    /// </summary>
    Task<string> GetPresignedUrlAsync(
        string bucketName,
        string objectKey,
        TimeSpan expiry,
        CancellationToken ct = default);
}
