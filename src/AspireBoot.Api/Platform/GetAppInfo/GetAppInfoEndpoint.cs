using System.Reflection;

using Microsoft.AspNetCore.Http.HttpResults;

namespace AspireBoot.Api.Platform.GetAppInfo;

internal sealed record GetAppInfoResponse(string Name, string Version, string Environment);

/// <summary>Public, cacheable metadata about the running API. Contains nothing sensitive.</summary>
internal static class GetAppInfoEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/info", Handle)
            .WithName("GetAppInfo")
            .WithSummary("Returns public metadata about the running API.")
            .AllowAnonymous();

    internal static Ok<GetAppInfoResponse> Handle(IHostEnvironment environment)
    {
        var version = typeof(GetAppInfoEndpoint).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        return TypedResults.Ok(new GetAppInfoResponse("AspireBoot", version, environment.EnvironmentName));
    }
}