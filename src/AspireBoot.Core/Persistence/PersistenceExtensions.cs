using Microsoft.EntityFrameworkCore;

using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace AspireBoot.Core.Persistence;

public static class PersistenceExtensions
{
    /// <summary>Provider options shared by the application hosts and design-time tooling.</summary>
    public static DbContextOptionsBuilder UseAspireBootNpgsql(this DbContextOptionsBuilder options, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
    }
}