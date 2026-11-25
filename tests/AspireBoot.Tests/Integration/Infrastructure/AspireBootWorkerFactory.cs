extern alias WorkerApp;

using JasperFx.CommandLine;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AspireBoot.Tests.Integration.Infrastructure;

/// <summary>Hosts the real Worker in memory, wired to the containers from <see cref="InfrastructureFixture"/>.</summary>
public sealed class AspireBootWorkerFactory(InfrastructureFixture infrastructure) : WebApplicationFactory<WorkerApp::AspireBoot.Worker.Program>
{
    static AspireBootWorkerFactory()
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
        builder.UseSetting("Email:Host", infrastructure.SmtpHost);
        builder.UseSetting("Email:Port", infrastructure.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Email:UseTls", "false");
        builder.UseSetting("Email:FromAddress", "no-reply@aspireboot.test");
        builder.UseSetting("Storage:ServiceUrl", "http://localhost:9000");
        builder.UseSetting("Storage:AccessKey", "test");
        builder.UseSetting("Storage:SecretKey", "test-secret");
    }
}