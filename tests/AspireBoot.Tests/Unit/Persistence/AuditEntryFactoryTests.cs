using System.Text.Json;

using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;
using AspireBoot.Core.Persistence.Auditing;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace AspireBoot.Tests.Unit.Persistence;

[Trait("Category", "Unit")]
public sealed class AuditEntryFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.CreateVersion7();

    [Fact]
    public void Insert_RecordsNewValuesAndMasksSecrets()
    {
        using var context = CreateContext();
        var user = NewUser();
        user.SecurityStamp = "super-secret-stamp";
        user.PasswordHash = "super-secret-hash";
        context.Users.Add(user);

        var row = Assert.Single(Create(context));

        Assert.Equal(AuditEntry.ActionInsert, row.Action);
        Assert.Equal(AuditEntry.ActorUser, row.ActorType);
        Assert.Equal(Actor, row.ActorUserId);
        Assert.Equal("AspNetUsers", row.TableName);
        Assert.Equal("User", row.EntityType);
        Assert.Equal(user.Id.ToString(), row.EntityKey);
        Assert.Equal(Now, row.OccurredAt);
        Assert.DoesNotContain("super-secret", row.Changes, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(row.Changes);
        Assert.Equal("ada@example.com", json.RootElement.GetProperty("Email").GetProperty("new").GetString());
        Assert.Equal("***", json.RootElement.GetProperty("SecurityStamp").GetProperty("new").GetString());
        Assert.Equal("***", json.RootElement.GetProperty("PasswordHash").GetProperty("new").GetString());
        Assert.Equal("***", json.RootElement.GetProperty("NormalizedEmail").GetProperty("new").GetString());
        Assert.False(json.RootElement.GetProperty("Email").TryGetProperty("old", out _));
    }

    [Fact]
    public void Insert_WithoutActor_IsAttributedToTheSystem()
    {
        using var context = CreateContext();
        context.Users.Add(NewUser());

        var row = Assert.Single(AuditEntryFactory.Create(context.ChangeTracker.Entries(), null, Now, "trace-1"));

        Assert.Equal(AuditEntry.ActorSystem, row.ActorType);
        Assert.Null(row.ActorUserId);
        Assert.Equal("trace-1", row.TraceId);
    }

    [Fact]
    public void Update_RecordsOnlyModifiedPropertiesWithOldAndNew()
    {
        using var context = CreateContext();
        var user = NewUser();
        context.Attach(user);
        user.Email = "grace@example.com";

        var row = Assert.Single(Create(context));

        Assert.Equal(AuditEntry.ActionUpdate, row.Action);
        using var json = JsonDocument.Parse(row.Changes);
        var email = json.RootElement.GetProperty("Email");
        Assert.Equal("ada@example.com", email.GetProperty("old").GetString());
        Assert.Equal("grace@example.com", email.GetProperty("new").GetString());
        Assert.Single(json.RootElement.EnumerateObject());
    }

    [Fact]
    public void Update_OfMaskedProperty_RecordsThatItChangedNeverTheValue()
    {
        using var context = CreateContext();
        var user = NewUser();
        user.SecurityStamp = "old-stamp";
        context.Attach(user);
        user.SecurityStamp = "new-stamp";

        var row = Assert.Single(Create(context));

        Assert.DoesNotContain("-stamp", row.Changes, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(row.Changes);
        var stamp = json.RootElement.GetProperty("SecurityStamp");
        Assert.Equal("***", stamp.GetProperty("old").GetString());
        Assert.Equal("***", stamp.GetProperty("new").GetString());
    }

    [Fact]
    public void Update_WithNoRealChange_ProducesNoRow()
    {
        using var context = CreateContext();
        var user = NewUser();
        context.Attach(user);
        context.Entry(user).State = EntityState.Modified;
        context.Entry(user).Property(u => u.Email).CurrentValue = user.Email;

        Assert.Empty(Create(context));
    }

    [Fact]
    public void Delete_RecordsOldValues()
    {
        using var context = CreateContext();
        var user = NewUser();
        context.Attach(user);
        context.Remove(user);

        var row = Assert.Single(Create(context));

        Assert.Equal(AuditEntry.ActionDelete, row.Action);
        using var json = JsonDocument.Parse(row.Changes);
        var email = json.RootElement.GetProperty("Email");
        Assert.Equal("ada@example.com", email.GetProperty("old").GetString());
        Assert.False(email.TryGetProperty("new", out _));
    }

    [Fact]
    public void ExcludedTypes_AreSkipped()
    {
        using var context = CreateContext();
        context.Add(new Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey { FriendlyName = "k", Xml = "<x/>" });

        Assert.Empty(Create(context));
    }

    [Fact]
    public void CompositeKey_IsSerializedAsJson_AndProviderKeyIsMasked()
    {
        using var context = CreateContext();
        var userId = Guid.CreateVersion7();
        context.Add(new IdentityUserLogin<Guid> { LoginProvider = "idp", ProviderKey = "external-secret-id", UserId = userId });

        var row = Assert.Single(Create(context));

        Assert.DoesNotContain("external-secret-id", row.Changes + row.EntityKey, StringComparison.Ordinal);
        using var key = JsonDocument.Parse(row.EntityKey);
        Assert.Equal("idp", key.RootElement.GetProperty("LoginProvider").GetString());
        Assert.Equal("***", key.RootElement.GetProperty("ProviderKey").GetString());
    }

    [Fact]
    public void UserToken_ValueIsMasked()
    {
        using var context = CreateContext();
        context.Add(new IdentityUserToken<Guid> { UserId = Guid.CreateVersion7(), LoginProvider = "p", Name = "n", Value = "token-secret" });

        var row = Assert.Single(Create(context));

        Assert.DoesNotContain("token-secret", row.Changes, StringComparison.Ordinal);
    }

    [Fact]
    public void Passkey_Insert_FoldsOwnedDataIntoOwnerRowWithoutValues()
    {
        using var context = CreateContext();
        var passkey = NewPasskey(Guid.CreateVersion7());
        context.Add(passkey);

        var row = Assert.Single(Create(context));

        Assert.Equal("AspNetUserPasskeys", row.TableName);
        Assert.Equal("IdentityUserPasskey`1", row.EntityType.Split('[')[0]);
        Assert.Equal(Convert.ToBase64String(passkey.CredentialId), row.EntityKey);
        Assert.DoesNotContain("Laptop", row.Changes, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(passkey.Data.PublicKey), row.Changes, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(row.Changes);
        Assert.True(json.RootElement.GetProperty("data").GetProperty("changed").GetBoolean());
    }

    [Fact]
    public void Passkey_OwnedDataChange_ProducesOneUpdateRowForTheOwner()
    {
        using var context = CreateContext();
        var passkey = NewPasskey(Guid.CreateVersion7());
        context.Attach(passkey);
        passkey.Data.SignCount = 42;

        var row = Assert.Single(Create(context));

        Assert.Equal(AuditEntry.ActionUpdate, row.Action);
        Assert.Equal(Convert.ToBase64String(passkey.CredentialId), row.EntityKey);
        Assert.DoesNotContain("42", row.Changes, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(row.Changes);
        Assert.True(json.RootElement.GetProperty("data").GetProperty("changed").GetBoolean());
    }

    [Fact]
    public void KeylessEntity_IsSkipped()
    {
        using var context = new KeylessContext();
        var keyless = context.Model.FindEntityType(typeof(Keyless))!;

        Assert.False(AuditPolicy.IsAudited(keyless));
    }

    [Fact]
    public void TemporaryKey_ThrowsInvalidOperation()
    {
        using var context = new IdentityKeyContext();
        context.Add(new Generated { Name = "x" });

        Assert.Throws<InvalidOperationException>(() => Create(context));
    }

    private static List<AuditEntry> Create(DbContext context) =>
        [.. AuditEntryFactory.Create(context.ChangeTracker.Entries(), Actor, Now, null)];

    internal static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseAspireBootNpgsql("Host=localhost;Database=never-opened").UseStandaloneIdentitySchema();
        return new AppDbContext(options.Options);
    }

    private static User NewUser()
    {
        var user = User.Create("ada@example.com", DisplayName.Create("Ada").Value, UiCulture.Default, new FakeTimeProvider(Now));
        user.NormalizedEmail = "ADA@EXAMPLE.COM";
        user.NormalizedUserName = "ADA@EXAMPLE.COM";
        return user;
    }

    private static IdentityUserPasskey<Guid> NewPasskey(Guid userId) => new()
    {
        CredentialId = [1, 2, 3, 4, 5],
        UserId = userId,
        Data = new IdentityPasskeyData
        {
            PublicKey = [9, 8, 7, 6],
            Name = "Laptop",
            CreatedAt = Now,
            SignCount = 1,
            Transports = ["internal"],
            IsUserVerified = true,
            IsBackupEligible = false,
            IsBackedUp = false,
            AttestationObject = [5, 5],
            ClientDataJson = [6, 6],
        },
    };

    public sealed class Keyless
    {
        public string Name { get; set; } = "";
    }

    public sealed class Generated
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    private sealed class KeylessContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseNpgsql("Host=localhost;Database=never-opened");

        protected override void OnModelCreating(ModelBuilder builder) => builder.Entity<Keyless>().HasNoKey();
    }

    private sealed class IdentityKeyContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseNpgsql("Host=localhost;Database=never-opened");

        protected override void OnModelCreating(ModelBuilder builder) => builder.Entity<Generated>().HasKey(e => e.Id);
    }
}