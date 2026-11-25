using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace AspireBoot.Core.Persistence;

public static class PersistenceExtensions
{
    /// <summary>Identity schema v3 adds the passkey table (AspNetUserPasskeys). Used by the API and by standalone contexts.</summary>
    public static readonly Version IdentitySchemaVersion = IdentitySchemaVersions.Version3;

    /// <summary>Provider options shared by the application hosts and design-time tooling.</summary>
    public static DbContextOptionsBuilder UseAspireBootNpgsql(this DbContextOptionsBuilder options, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
    }

    /// <summary>
    /// Identity reads its store options (schema version) from the service provider while the model is built.
    /// Contexts created without the host (design-time tooling, tests) use this provider so they map the same schema as the API.
    /// </summary>
    public static DbContextOptionsBuilder UseStandaloneIdentitySchema(this DbContextOptionsBuilder options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var services = new ServiceCollection()
            .AddOptions<IdentityOptions>()
            .Configure(identity => identity.Stores.SchemaVersion = IdentitySchemaVersion)
            .Services
            .BuildServiceProvider();

        return options.UseApplicationServiceProvider(services);
    }
}