using JasperFx.CommandLine;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AspireBoot.Tests.Integration.Infrastructure;

/// <summary>Hosts the real API in memory, wired to the containers from <see cref="InfrastructureFixture"/>.</summary>
public sealed class AspireBootApiFactory(InfrastructureFixture infrastructure) : WebApplicationFactory<Program>
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
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Email:Host"] = "localhost",
            ["Email:Port"] = "1025",
            ["Email:UseTls"] = "false",
            ["Email:FromAddress"] = "no-reply@aspireboot.test",
            ["Storage:ServiceUrl"] = "http://localhost:9000",
            ["Storage:AccessKey"] = "test",
            ["Storage:SecretKey"] = "test-secret",
        }));
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });
}