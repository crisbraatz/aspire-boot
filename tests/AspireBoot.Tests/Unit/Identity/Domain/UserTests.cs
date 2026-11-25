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

        var user = User.Create("ada@example.com", name, UiCulture.Default, clock);

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("ada@example.com", user.Email);
        Assert.Equal("ada@example.com", user.UserName);
        Assert.Equal(name, user.DisplayName);
        Assert.Equal(Now, user.CreatedAt);
        Assert.False(user.EmailConfirmed);
    }

    [Fact]
    public void Create_StoresPreferredCulture()
    {
        var culture = UiCulture.Create("pt-BR").Value;

        var user = User.Create("ada@example.com", DisplayName.Create("Ada").Value, culture, new FakeTimeProvider(Now));

        Assert.Equal(culture, user.PreferredCulture);
    }

    [Fact]
    public void ChangePreferredCulture_ReplacesCulture()
    {
        var user = User.Create("ada@example.com", DisplayName.Create("Ada").Value, UiCulture.Default, new FakeTimeProvider(Now));
        var spanish = UiCulture.Create("es-ES").Value;

        user.ChangePreferredCulture(spanish);

        Assert.Equal(spanish, user.PreferredCulture);
    }

    [Fact]
    public void EnsureCanRemovePasskey_WithOneRemainingPasskey_FailsWithLastPasskey()
    {
        var user = User.Create("ada@example.com", DisplayName.Create("Ada").Value, UiCulture.Default, new FakeTimeProvider(Now));

        var result = user.EnsureCanRemovePasskey(1);

        Assert.False(result.IsSuccess);
        Assert.Equal("Identity.LastPasskey", result.Failure!.Code);
    }

    [Fact]
    public void EnsureCanRemovePasskey_WithNoPasskeys_FailsWithLastPasskey()
    {
        var user = User.Create("ada@example.com", DisplayName.Create("Ada").Value, UiCulture.Default, new FakeTimeProvider(Now));

        var result = user.EnsureCanRemovePasskey(0);

        Assert.Equal("Identity.LastPasskey", result.Failure!.Code);
    }

    [Fact]
    public void EnsureCanRemovePasskey_WithTwoPasskeys_Succeeds()
    {
        var user = User.Create("ada@example.com", DisplayName.Create("Ada").Value, UiCulture.Default, new FakeTimeProvider(Now));

        var result = user.EnsureCanRemovePasskey(2);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Rename_ReplacesDisplayName()
    {
        var user = User.Create("ada@example.com", DisplayName.Create("Ada").Value, UiCulture.Default, new FakeTimeProvider(Now));
        var newName = DisplayName.Create("Ada Lovelace").Value;

        user.Rename(newName);

        Assert.Equal(newName, user.DisplayName);
    }
}