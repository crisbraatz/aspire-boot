using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

using Npgsql;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Shared defaults for every AspireBoot service: OpenTelemetry, health checks,
/// service discovery and HTTP resilience. Keep this project free of business code.
/// </summary>
public static class Extensions
{
    public const string HealthEndpointPath = "/health";
    public const string AlivenessEndpointPath = "/alive";

    /// <summary>Prefix shared by every ActivitySource and Meter owned by AspireBoot.</summary>
    public const string TelemetryPrefix = "AspireBoot";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOpenTelemetry()
            .WithLogging(_ => { }, logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
            })
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddNpgsqlInstrumentation()
                .AddMeter($"{TelemetryPrefix}.*")
                .AddMeter("Wolverine*"))
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddSource($"{TelemetryPrefix}.*")
                .AddSource("Wolverine")
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments(HealthEndpointPath, StringComparison.Ordinal)
                        && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath, StringComparison.Ordinal))
                .AddHttpClientInstrumentation()
                .AddNpgsql());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps /health (readiness: every check) and /alive (liveness: "live" checks).
    /// These endpoints are served on the internal network only; the public reverse proxy never exposes them.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks(HealthEndpointPath).AllowAnonymous();
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live"),
        }).AllowAnonymous();

        return app;
    }
}