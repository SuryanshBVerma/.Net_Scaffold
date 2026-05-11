# FastEndpoints & REPR Pattern

## What is REPR?

**REPR** = **R**equest → **E**ndpoint → **P**arameter → **R**esponse

It's a structured alternative to MVC Controllers. Instead of one controller class with many action methods, each HTTP operation gets its own dedicated class:

| MVC | REPR |
|---|---|
| `ProductsController` with 6 methods | 6 separate endpoint classes |
| Shared constructor bloat | Each class has exactly what it needs |
| Hard to unit test in isolation | Each endpoint is a plain class — trivial to test |
| Fat controllers over time | Every endpoint stays small by design |

---

## Anatomy of an Endpoint

```csharp
// Request DTO — what the client sends
public sealed record CreateProductHttpRequest(
    string  Name,
    string? Description,
    decimal Price,
    Guid    CategoryId);

// Endpoint — one class per HTTP operation
public sealed class CreateProductEndpoint(IProductService products)
    : Endpoint<CreateProductHttpRequest, ProductDetail>
{
    // 1. Configure — declares route, verb, auth, Swagger metadata
    public override void Configure()
    {
        Post("/api/products");
        // No AllowAnonymous() → authentication required by default in FastEndpoints
        Description(d => d.WithSummary("Create a new product."));
    }

    // 2. HandleAsync — pure business logic, no routing concerns
    public override async Task HandleAsync(CreateProductHttpRequest req, CancellationToken ct)
    {
        var result = await products.CreateAsync(
            new CreateProductRequest(req.Name, req.Description, req.Price, req.CategoryId), ct);

        await this.SendMappedResultAsync(result, ct);
    }
}
```

**Key points:**
- `Configure()` is called once at startup to build the routing table
- `HandleAsync()` is called per request — keep it focused on the use case
- Constructor injection works normally — FastEndpoints resolves from DI
- The generic parameters `<TRequest, TResponse>` drive model binding and serialization automatically

---

## AllowAnonymous vs Default Auth

FastEndpoints requires authentication by default when `AddNexaCommerceAuth()` is called. Override per-endpoint:

```csharp
// Public endpoint — anyone can call this
public override void Configure()
{
    Get("/api/products");
    AllowAnonymous();
}

// Protected endpoint — valid JWT required
public override void Configure()
{
    Post("/api/products");
    // No AllowAnonymous() → 401 if no valid token
}
```

---

## ResultExtensions — Mapping Domain Results to HTTP

Domain services return `ResultStatus` (from the `Ardalis.Result` pattern) rather than throwing exceptions. `SendMappedResultAsync` translates these to the correct HTTP status codes:

```csharp
public static class ResultExtensions
{
    public static async Task SendMappedResultAsync<T>(
        this IEndpoint ep, Result<T> result, CancellationToken ct)
    {
        switch (result.Status)
        {
            case ResultStatus.Ok:
                await ep.HttpContext.Response.SendOkAsync(result.Value, ct);
                break;
            case ResultStatus.Created:
                await ep.HttpContext.Response.SendCreatedAtAsync(...);
                break;
            case ResultStatus.NotFound:
                await ep.HttpContext.Response.SendNotFoundAsync(ct);
                break;
            case ResultStatus.Invalid:
                await ep.HttpContext.Response.SendErrorsAsync(result.ValidationErrors, ct);
                break;
            case ResultStatus.Unauthorized:
                await ep.HttpContext.Response.SendUnauthorizedAsync(ct);
                break;
        }
    }
}
```

**Why this pattern?**
- Domain services never know about HTTP — they return `Result<T>`
- Endpoints never contain `if/switch` for status codes — they delegate to the extension
- Adding a new status code is one change in one place

---

## Swagger / OpenAPI

FastEndpoints generates OpenAPI automatically from your endpoint classes. Access Swagger UI at `/swagger` in development. The setup in SharedKernel:

```csharp
services.AddNexaFastEndpoints();
// → internally calls:
//   services.AddFastEndpoints()
//   services.SwaggerDocument(...)
```

In the middleware pipeline:
```csharp
app.UseNexaCommerceDefaults();
// → internally calls:
//   app.UseFastEndpoints()
//   app.UseSwaggerGen()  (development only)
```

---

## Request Validation

FastEndpoints integrates FluentValidation. Create a validator class alongside the endpoint:

```csharp
public sealed class CreateProductValidator : Validator<CreateProductHttpRequest>
{
    public CreateProductValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Price).GreaterThan(0);
        RuleFor(r => r.CategoryId).NotEmpty();
    }
}
```

FastEndpoints auto-discovers and runs this before `HandleAsync`. Invalid requests return 400 with structured error messages — no code in `HandleAsync` needed.
