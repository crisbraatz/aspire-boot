using AspireBoot.Api.Platform.GetAppInfo;

namespace AspireBoot.Api.Shared;

/// <summary>The single place where bounded-context route groups are created and slices are mapped.</summary>
internal static class EndpointMapping
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api")
            .AddEndpointFilter<AntiforgeryValidationFilter>();

        var platform = api.MapGroup("/platform").WithTags("Platform");
        GetAppInfoEndpoint.Map(platform);

        return app;
    }
}