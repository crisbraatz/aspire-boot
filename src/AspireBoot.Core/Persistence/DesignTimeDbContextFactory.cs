using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AspireBoot.Core.Persistence;

/// <summary>Used only by `dotnet ef` to create migrations. No real database connection is opened.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseAspireBootNpgsql("Host=localhost;Database=aspireboot");

        return new AppDbContext(options.Options);
    }
}