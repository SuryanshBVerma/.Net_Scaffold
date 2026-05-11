# MinIO Object Storage

## What is MinIO?

MinIO is an **S3-compatible** object storage server you can run locally or self-host. The API is identical to Amazon S3 — the same SDK works against both. In NexaCommerce it stores product images.

**Why MinIO instead of Azure Blob or AWS S3?**
- Zero cloud cost for local development
- Docker image, starts in seconds
- Drop-in S3 compatible — switching to real AWS S3 in production is one connection string change

---

## The Abstraction

`IObjectStorageService` in SharedKernel shields the domain from the storage provider:

```csharp
public interface IObjectStorageService
{
    Task<string> UploadAsync(string bucket, string key, Stream content,
                             string contentType, CancellationToken ct = default);

    Task<Stream> DownloadAsync(string bucket, string key,
                               CancellationToken ct = default);

    Task DeleteAsync(string bucket, string key, CancellationToken ct = default);
}
```

The production implementation is `MinioObjectStorageService`. In unit tests, `IObjectStorageService` is mocked — no Docker required.

---

## MinioObjectStorageService

```csharp
public sealed class MinioObjectStorageService(
    IMinioClient                    minio,
    ILogger<MinioObjectStorageService> logger) : IObjectStorageService
{
    public async Task<string> UploadAsync(
        string bucket, string key, Stream content,
        string contentType, CancellationToken ct = default)
    {
        // Ensure bucket exists (idempotent)
        var bucketExists = await minio.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(bucket), ct);

        if (!bucketExists)
            await minio.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(bucket), ct);

        await minio.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(key)
            .WithStreamData(content)
            .WithObjectSize(content.Length)
            .WithContentType(contentType), ct);

        logger.LogInformation("Uploaded {Key} to bucket {Bucket}", key, bucket);
        return key;
    }
}
```

---

## Configuration

In Aspire (`appsettings.json`):
```json
"Minio": {
  "Tag":              "latest",
  "RootUser":         "minioadmin",
  "RootPassword":     "minioadmin",
  "DataVolume":       "nexacommerce-minio-data",
  "S3ApiTargetPort":  9000,
  "ConsoleTargetPort": 9001
}
```

In `appsettings.Development.json` for the API (running without Aspire):
```json
"Storage": {
  "ServiceUrl": "http://localhost:9000",
  "AccessKey":  "minioadmin",
  "SecretKey":  "minioadmin"
}
```

`MinioObjectStorageService` reads these via `IConfiguration`:
```csharp
builder.Services.AddSingleton<IMinioClient>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    return new MinioClient()
        .WithEndpoint(cfg["Storage:ServiceUrl"]!)
        .WithCredentials(cfg["Storage:AccessKey"]!, cfg["Storage:SecretKey"]!)
        .Build();
});
```

---

## Image Upload Endpoint

```csharp
public sealed class UploadProductImageEndpoint(IProductService products)
    : Endpoint<UploadImageRequest>
{
    public override void Configure()
    {
        Put("/api/products/{id}/image");
        AllowFileUploads();   // enables multipart/form-data parsing
    }

    public override async Task HandleAsync(UploadImageRequest req, CancellationToken ct)
    {
        var file = Files.FirstOrDefault();
        if (file is null) { await SendErrorsAsync(400, ct); return; }

        var key = $"products/{req.Id}/{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var result = await products.UploadImageAsync(req.Id, key, file.OpenReadStream(),
                                                     file.ContentType, ct);
        await this.SendMappedResultAsync(result, ct);
    }
}
```

---

## MinIO Console

Access the MinIO web console at `http://localhost:9001` (when running via Aspire or docker-compose).

- Default credentials: `minioadmin` / `minioadmin`
- Browse buckets, upload/download files, manage policies
- Useful for verifying image uploads during development
