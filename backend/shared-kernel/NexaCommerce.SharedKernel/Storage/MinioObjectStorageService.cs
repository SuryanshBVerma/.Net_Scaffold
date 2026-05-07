using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace NexaCommerce.SharedKernel.Storage;

/// <summary>
/// S3-compatible implementation using the AWS SDK.
///
/// LEARNING — Why AWSSDK.S3 against MinIO?
///   MinIO implements the full Amazon S3 API. The AWS SDK sends standard
///   S3 REST requests. By pointing the SDK's ServiceURL at MinIO's endpoint
///   instead of AWS, you get identical behaviour locally.
///
///   Production: remove ServiceURL → SDK talks to real AWS S3 automatically.
///   Development: set ServiceURL = "http://localhost:9000" (MinIO via Aspire).
///
///   Configuration keys (injected by Aspire or appsettings):
///     Storage:ServiceUrl     → MinIO endpoint (e.g. "http://localhost:9000")
///     Storage:AccessKey      → MinIO access key (default: "minioadmin")
///     Storage:SecretKey      → MinIO secret key (default: "minioadmin")
///     Storage:UsePathStyle   → true (required for MinIO; AWS uses virtual-hosted)
/// </summary>
public sealed class MinioObjectStorageService : IObjectStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly string _publicBaseUrl;

    public MinioObjectStorageService(IConfiguration configuration)
    {
        var serviceUrl    = configuration["Storage:ServiceUrl"] ?? "http://localhost:9000";
        var accessKey     = configuration["Storage:AccessKey"]  ?? "minioadmin";
        var secretKey     = configuration["Storage:SecretKey"]  ?? "minioadmin";
        var usePathStyle  = bool.Parse(configuration["Storage:UsePathStyle"] ?? "true");

        // LEARNING: AmazonS3Config lets you point the SDK at any S3-compatible endpoint.
        // ForcePathStyle = true is required for MinIO (and many S3-compatible stores).
        // AWS itself uses virtual-hosted style (bucket.s3.amazonaws.com), but MinIO
        // uses path style (localhost:9000/bucket). Set UsePathStyle=false in production.
        _s3 = new AmazonS3Client(
            accessKey,
            secretKey,
            new AmazonS3Config
            {
                ServiceURL    = serviceUrl,
                ForcePathStyle = usePathStyle
            });

        _publicBaseUrl = serviceUrl;
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(
        string bucketName,
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken ct = default)
    {
        // Ensure the bucket exists — idempotent, safe to call every time.
        await EnsureBucketExistsAsync(bucketName, ct);

        var request = new PutObjectRequest
        {
            BucketName  = bucketName,
            Key         = objectKey,
            InputStream = content,
            ContentType = contentType,
            // LEARNING: ServerSideEncryptionMethod.AES256 enables at-rest encryption.
            // MinIO supports this. In production on AWS, use SSE-S3 or SSE-KMS.
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        };

        await _s3.PutObjectAsync(request, ct);

        // Return the direct URL. In production, return a CloudFront or CDN URL.
        return $"{_publicBaseUrl}/{bucketName}/{objectKey}";
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(
        string bucketName,
        string objectKey,
        CancellationToken ct = default)
    {
        var response = await _s3.GetObjectAsync(bucketName, objectKey, ct);
        return response.ResponseStream;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string bucketName,
        string objectKey,
        CancellationToken ct = default)
    {
        // LEARNING: DeleteObjectAsync does NOT throw if the key doesn't exist.
        // This is idempotent — safe to call in cleanup jobs without defensive checks.
        await _s3.DeleteObjectAsync(bucketName, objectKey, ct);
    }

    /// <inheritdoc />
    public async Task<string> GetPresignedUrlAsync(
        string bucketName,
        string objectKey,
        TimeSpan expiry,
        CancellationToken ct = default)
    {
        // LEARNING: GetPreSignedURL is synchronous in the SDK but the URL calculation
        // is pure HMAC signing — no network call needed.
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key        = objectKey,
            Expires    = DateTime.UtcNow.Add(expiry),
            Protocol   = Protocol.HTTP  // Use HTTPS in production.
        };

        return _s3.GetPreSignedURL(request);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task EnsureBucketExistsAsync(string bucketName, CancellationToken ct)
    {
        var exists = await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(_s3, bucketName);
        if (!exists)
        {
            await _s3.PutBucketAsync(new PutBucketRequest
            {
                BucketName = bucketName,
                UseClientRegion = true
            }, ct);
        }
    }
}
