using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;

namespace AspireBoot.Api.Shared;

/// <summary>
/// Applies the body size limit declared as endpoint metadata (for example on the /api/identity group). Minimal APIs
/// do not enforce <see cref="IRequestSizeLimitMetadata"/> by themselves, so this middleware runs after routing and
/// before any body is bound. It refuses a declared Content-Length above the limit with 413 Problem Details and sets
/// the server's body limit, which also covers bodies sent without Content-Length.
/// </summary>
internal static class RequestSizeLimiting
{
    public static IApplicationBuilder UseEndpointRequestSizeLimits(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var limit = context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize;
            if (limit is not { } maxBytes)
            {
                await next(context);
                return;
            }

            if (context.Request.ContentLength is { } declared && declared > maxBytes)
            {
                await TypedResults.Problem(
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    title: "The request body is too large.",
                    type: "request.tooLarge").ExecuteAsync(context);
                return;
            }

            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
            {
                bodySize.MaxRequestBodySize = maxBytes;
            }

            await next(context);
        });
}