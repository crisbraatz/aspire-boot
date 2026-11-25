using AspireBoot.Api.Shared;

using JasperFx.CommandLine;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AspireBoot.Tests.Integration.Infrastructure;

/// <summary>Hosts the real API in memory, wired to the containers from <see cref="InfrastructureFixture"/>.</summary>
public class AspireBootApiFactory(
    InfrastructureFixture infrastructure,
    IReadOnlyDictionary<string, string?>? settingOverrides = null) : WebApplicationFactory<global::Program>
{
    static AspireBootApiFactory()
    {
        // Program ends with RunJasperFxCommands; this makes it start the host for WebApplicationFactory.
        JasperFxEnvironment.AutoStartHost = true;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:aspireboot", infrastructure.PostgresConnectionString);
        builder.UseSetting("ConnectionStrings:rabbitmq", infrastructure.RabbitMqConnectionString);
        builder.UseSetting("ConnectionStrings:redis", infrastructure.RedisConnectionString);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(BuildSettings()));
    }

    /// <summary>Test defaults: high rate limits so the suite never sees 429, then per-test overrides.</summary>
    private Dictionary<string, string?> BuildSettings()
    {
        var settings = new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Email:Host"] = infrastructure.SmtpHost,
            ["Email:Port"] = infrastructure.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Email:UseTls"] = "false",
            ["Email:FromAddress"] = "no-reply@aspireboot.test",
            ["Storage:ServiceUrl"] = "http://localhost:9000",
            ["Storage:AccessKey"] = "test",
            ["Storage:SecretKey"] = "test-secret",
            ["App:PublicBaseUrl"] = "https://localhost",
            ["Passkeys:ServerDomain"] = "localhost",
            ["Passkeys:AllowedOrigins:0"] = "https://localhost",
            ["Identity:EmailCooldownSeconds"] = "1",
        };

        foreach (var policy in RateLimitPolicies.All)
        {
            settings[$"RateLimiting:{policy}:PermitLimit"] = "10000";
        }

        foreach (var (key, value) in settingOverrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        return settings;
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });
}