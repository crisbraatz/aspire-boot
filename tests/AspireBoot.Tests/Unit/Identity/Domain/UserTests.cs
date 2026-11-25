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

    [Fact]
    public void ChangeAvatar_FirstUpload_SetsAvatarAndVersionOne()
    {
        var user = NewUser();
        var objectId = Guid.NewGuid();

        var superseded = user.ChangeAvatar(objectId);

        Assert.Null(superseded);
        Assert.Equal(1, user.AvatarVersion);
        Assert.Equal(AvatarReference.For(user.Id, objectId, 1), user.Avatar);
    }

    [Fact]
    public void ChangeAvatar_Replace_ReturnsSupersededReferenceAndIncrementsVersion()
    {
        var user = NewUser();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        user.ChangeAvatar(first);
        var previous = user.Avatar;

        var superseded = user.ChangeAvatar(second);

        Assert.Equal(previous, superseded);
        Assert.Equal(2, user.AvatarVersion);
        Assert.Equal(AvatarReference.For(user.Id, second, 2), user.Avatar);
    }

    [Fact]
    public void RemoveAvatar_WithAvatar_ReturnsReferenceClearsAvatarAndKeepsVersion()
    {
        var user = NewUser();
        user.ChangeAvatar(Guid.NewGuid());
        var current = user.Avatar;

        var superseded = user.RemoveAvatar();

        Assert.Equal(current, superseded);
        Assert.Null(user.Avatar);
        Assert.Equal(1, user.AvatarVersion);
    }

    [Fact]
    public void RemoveAvatar_WithoutAvatar_ReturnsNullAndChangesNothing()
    {
        var user = NewUser();

        var superseded = user.RemoveAvatar();

        Assert.Null(superseded);
        Assert.Null(user.Avatar);
        Assert.Equal(0, user.AvatarVersion);
    }

    [Fact]
    public void RemoveAvatar_Twice_IsIdempotent()
    {
        var user = NewUser();
        user.ChangeAvatar(Guid.NewGuid());
        user.RemoveAvatar();

        var superseded = user.RemoveAvatar();

        Assert.Null(superseded);
        Assert.Equal(1, user.AvatarVersion);
    }

    [Fact]
    public void VersionNeverReused_AcrossRemoveAndUpload()
    {
        var user = NewUser();
        user.ChangeAvatar(Guid.NewGuid());
        user.RemoveAvatar();

        user.ChangeAvatar(Guid.NewGuid());

        Assert.Equal(2, user.AvatarVersion);
    }

    private static User NewUser() =>
        User.Create("ada@example.com", DisplayName.Create("Ada").Value, UiCulture.Default, new FakeTimeProvider(Now));
}