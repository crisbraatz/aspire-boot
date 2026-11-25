using Microsoft.AspNetCore.Antiforgery;

namespace AspireBoot.Api.Shared;

/// <summary>
/// Validates the X-XSRF-TOKEN header on every unsafe request under /api.
/// Minimal APIs only auto-validate form posts, so JSON endpoints need this explicit check.
/// </summary>
internal sealed class AntiforgeryValidationFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;

        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
        {
            return await next(context);
        }

        if (!await antiforgery.IsRequestValidAsync(context.HttpContext))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid or missing antiforgery token.",
                type: "antiforgery.invalid");
        }

        return await next(context);
    }
}