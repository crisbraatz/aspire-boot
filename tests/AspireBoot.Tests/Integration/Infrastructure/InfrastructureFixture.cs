using AspireBoot.Core.Persistence;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Respawn;

using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace AspireBoot.Tests.Integration.Infrastructure;

/// <summary>
/// Real infrastructure for integration tests, started once per test run.
/// Image tags match deploy/compose.yaml and the AppHost.
/// </summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:19.0-alpine")
        .WithDatabase("aspireboot")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.4.1-management-alpine").Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:8.12.0-alpine").Build();

    // Same image as the AppHost (CommunityToolkit.Aspire.Hosting.Mailpit 17.0.0 with Mailpit v1.34).
    private const int MailpitSmtpPort = 1025;
    private const int MailpitHttpPort = 8025;

    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:v1.34")
        .WithPortBinding(MailpitSmtpPort, true)
        .WithPortBinding(MailpitHttpPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
            .ForPort(MailpitHttpPort)
            .ForPath("/api/v1/info")))
        .Build();

    // Same image as deploy/compose.yaml. Test keys only; the S3 port is 9000.
    private const int RustFsPort = 9000;

    private const string RustFsAccessKey = "rustfs-test-access";
    private const string RustFsSecretKey = "rustfs-test-secret-key";

    private readonly IContainer _rustFs = new ContainerBuilder("rustfs/rustfs:1.0.6")
        .WithPortBinding(RustFsPort, true)
        .WithEnvironment("RUSTFS_ACCESS_KEY", RustFsAccessKey)
        .WithEnvironment("RUSTFS_SECRET_KEY", RustFsSecretKey)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
            .ForPort(RustFsPort)
            .ForPath("/health")))
        .Build();

    private HttpClient? _mailpitHttp;
    private MailpitClient? _mailpitClient;

    private Respawner? _respawner;

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();

    public string RedisConnectionString => _redis.GetConnectionString();

    public string SmtpHost => _mailpit.Hostname;

    public int SmtpPort => _mailpit.GetMappedPublicPort(MailpitSmtpPort);

    public string StorageServiceUrl => $"http://{_rustFs.Hostname}:{_rustFs.GetMappedPublicPort(RustFsPort)}";

    public string StorageAccessKey => RustFsAccessKey;

    public string StorageSecretKey => RustFsSecretKey;

    public string StorageBucket => "aspireboot-test";

    /// <summary>Mailpit HTTP API client for reading the mail sent during tests.</summary>
    public MailpitClient Mailpit => _mailpitClient
        ?? throw new InvalidOperationException("The fixture has not been initialized.");

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(
            _postgres.StartAsync(),
            _rabbitMq.StartAsync(),
            _redis.StartAsync(),
            _mailpit.StartAsync(),
            _rustFs.StartAsync());

        _mailpitHttp = new HttpClient
        {
            BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(MailpitHttpPort)}/"),
        };
        _mailpitClient = new MailpitClient(_mailpitHttp);

        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseAspireBootNpgsql(PostgresConnectionString).UseStandaloneIdentitySchema();
        await using (var context = new AppDbContext(options.Options))
        {
            await context.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = ["__ef_migrations_history"],
        });
    }

    /// <summary>Deletes all application data and captured mail. Call at the start of every test that writes data.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        await _respawner!.ResetAsync(connection);
        await Mailpit.DeleteAllAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
        await _redis.DisposeAsync();
        await _mailpit.DisposeAsync();
        await _rustFs.DisposeAsync();
        _mailpitHttp?.Dispose();
    }
}
/// <summary>
/// Collection fixture instead of an assembly fixture: unit tests must not start containers.
/// Every integration test class joins this collection and so shares one infrastructure and runs sequentially.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SharedInfrastructure : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "Infrastructure";
}