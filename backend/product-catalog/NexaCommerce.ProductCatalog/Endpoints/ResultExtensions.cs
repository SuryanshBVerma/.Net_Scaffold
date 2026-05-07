using Ardalis.Result;
using FastEndpoints;

namespace NexaCommerce.ProductCatalog.Endpoints;

/// <summary>
/// Maps Ardalis.Result status codes to FastEndpoints HTTP responses.
///
/// LEARNING: FastEndpoints doesn't ship built-in Ardalis.Result integration
/// in v8 (it's available in some editions/configs). This helper centralises
/// the mapping so every endpoint handles Result uniformly.
///
/// Mapping table:
///   ResultStatus.Ok          → 200 (or custom successStatusCode)
///   ResultStatus.NotFound    → 404
///   ResultStatus.Invalid     → 422 Unprocessable Entity
///   ResultStatus.Unauthorized → 401
///   ResultStatus.Forbidden   → 403
///   ResultStatus.Error       → 500
/// </summary>
internal static class ResultExtensions
{
    internal static async Task SendMappedResultAsync<T>(
        this IEndpoint ep, Result<T> result, CancellationToken ct, int successCode = 200)
    {
        if (result.IsSuccess)
        {
            await ep.HttpContext.Response.SendAsync(result.Value, successCode, cancellation: ct);
            return;
        }

        await SendError(ep, result.Status, result.Errors, ct);
    }

    internal static async Task SendMappedResultAsync(
        this IEndpoint ep, Result result, CancellationToken ct, int successCode = 204)
    {
        if (result.IsSuccess)
        {
            ep.HttpContext.Response.StatusCode = successCode;
            return;
        }

        await SendError(ep, result.Status, result.Errors, ct);
    }

    private static async Task SendError(
        IEndpoint ep, ResultStatus status, IEnumerable<string> errors, CancellationToken ct)
    {
        switch (status)
        {
            case ResultStatus.NotFound:
                await ep.HttpContext.Response.SendNotFoundAsync(cancellation: ct);
                break;
            case ResultStatus.Invalid:
                ep.HttpContext.Response.StatusCode = 422;
                await ep.HttpContext.Response.WriteAsJsonAsync(
                    new { errors = errors.ToArray() }, ct);
                break;
            case ResultStatus.Unauthorized:
                await ep.HttpContext.Response.SendUnauthorizedAsync(ct);
                break;
            case ResultStatus.Forbidden:
                await ep.HttpContext.Response.SendForbiddenAsync(ct);
                break;
            default:
                ep.HttpContext.Response.StatusCode = 500;
                await ep.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "An unexpected error occurred." }, ct);
                break;
        }
    }
}
