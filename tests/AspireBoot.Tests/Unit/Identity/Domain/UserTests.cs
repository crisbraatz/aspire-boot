using AspireBoot.Core.Identity.Domain;

using Microsoft.Extensions.Time.Testing;

namespace AspireBoot.Tests.Unit.Identity.Domain;

[Trait("Category", "Unit")]
public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_SetsIdentityFieldsAndCreationTime()
    {
        var clock = new FakeTimeProvider(Now);
        var name = DisplayName.Create("Ada Lovelace").Value;

        var user = User.Create("ada@example.com", name, clock);

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("ada@example.com", user.Email);
        Assert.Equal("ada@example.com", user.UserName);
        Assert.Equal(name, user.DisplayName);
        Assert.Equal(Now, user.CreatedAt);
        Assert.False(user.EmailConfirmed);
    }

    [Fact]
    public void Rename_ReplacesDisplayName()
    {
        var user = User.Create("ada@example.com", DisplayName.Create("Ada").Value, new FakeTimeProvider(Now));
        var newName = DisplayName.Create("Ada Lovelace").Value;

        user.Rename(newName);

        Assert.Equal(newName, user.DisplayName);
    }
}