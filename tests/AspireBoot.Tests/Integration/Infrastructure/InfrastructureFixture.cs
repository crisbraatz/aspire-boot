using AspireBoot.Core.Persistence;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Respawn;

using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(AspireBoot.Tests.Integration.Infrastructure.InfrastructureFixture))]

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

    private Respawner? _respawner;

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();

    public string RedisConnectionString => _redis.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync(), _redis.StartAsync());

        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseAspireBootNpgsql(PostgresConnectionString);
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

    /// <summary>Deletes all application data. Call at the start of every test that writes data.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        await _respawner!.ResetAsync(connection);
    }

    public async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
        await _redis.DisposeAsync();
    }
}