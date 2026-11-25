using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;
using AspireBoot.Core.Persistence.Auditing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace AspireBoot.Tests.Unit.Persistence;

[Trait("Category", "Unit")]
public sealed class AuditSaveChangesInterceptorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FailedSave_DetachesTheAuditRows_SoARetryDoesNotDuplicateThem()
    {
        var ct = TestContext.Current.CancellationToken;
        using var context = CreateContext();
        context.Users.Add(NewUser());

        await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync(ct));
        Assert.Empty(context.ChangeTracker.Entries<AuditEntry>());

        await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync(ct));
        Assert.Empty(context.ChangeTracker.Entries<AuditEntry>());
    }

    [Fact]
    public void FailedSyncSave_DetachesTheAuditRows()
    {
        using var context = CreateContext();
        context.Users.Add(NewUser());

        Assert.ThrowsAny<Exception>(() => context.SaveChanges());

        Assert.Empty(context.ChangeTracker.Entries<AuditEntry>());
    }

    private static AppDbContext CreateContext()
    {
        var interceptor = new AuditSaveChangesInterceptor(new SystemAuditActor(), new FakeTimeProvider(Now));
        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseAspireBootNpgsql("Host=127.0.0.1;Port=1;Database=never-opened;Timeout=1;Command Timeout=1")
            .UseStandaloneIdentitySchema()
            .AddInterceptors(interceptor);
        return new AppDbContext(options.Options);
    }

    private static User NewUser()
    {
        var user = User.Create("ada@example.com", DisplayName.Create("Ada").Value, UiCulture.Default, new FakeTimeProvider(Now));
        user.NormalizedEmail = "ADA@EXAMPLE.COM";
        user.NormalizedUserName = "ADA@EXAMPLE.COM";
        return user;
    }
}