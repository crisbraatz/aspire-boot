using AspireBoot.Core.Identity.Domain;

namespace AspireBoot.Tests.Unit.Identity.Domain;

[Trait("Category", "Unit")]
public sealed class AvatarReferenceTests
{
    [Fact]
    public void For_BuildsOwnerScopedWebpKey()
    {
        var userId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var objectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var reference = AvatarReference.For(userId, objectId, 3);

        Assert.Equal("avatars/11111111222233334444555555555555/aaaaaaaabbbbccccddddeeeeeeeeeeee.webp", reference.Key);
        Assert.Equal(3, reference.Version);
    }

    [Fact]
    public void For_WithVersionBelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AvatarReference.For(Guid.NewGuid(), Guid.NewGuid(), 0));
    }

    [Fact]
    public void For_WithEmptyUserId_Throws()
    {
        Assert.Throws<ArgumentException>(() => AvatarReference.For(Guid.Empty, Guid.NewGuid(), 1));
    }

    [Fact]
    public void For_WithEmptyObjectId_Throws()
    {
        Assert.Throws<ArgumentException>(() => AvatarReference.For(Guid.NewGuid(), Guid.Empty, 1));
    }

    [Fact]
    public void For_WithSameInputs_ProducesEqualReferences()
    {
        var userId = Guid.NewGuid();
        var objectId = Guid.NewGuid();

        Assert.Equal(AvatarReference.For(userId, objectId, 2), AvatarReference.For(userId, objectId, 2));
    }

    [Fact]
    public void KeyFor_BuildsTheSameKeyAsFor()
    {
        var userId = Guid.NewGuid();
        var objectId = Guid.NewGuid();

        Assert.Equal($"avatars/{userId:N}/{objectId:N}.webp", AvatarReference.KeyFor(userId, objectId));
        Assert.Equal(AvatarReference.KeyFor(userId, objectId), AvatarReference.For(userId, objectId, 7).Key);
    }

    [Fact]
    public void KeyFor_WithEmptyUserId_Throws()
    {
        Assert.Throws<ArgumentException>(() => AvatarReference.KeyFor(Guid.Empty, Guid.NewGuid()));
    }

    [Fact]
    public void KeyFor_WithEmptyObjectId_Throws()
    {
        Assert.Throws<ArgumentException>(() => AvatarReference.KeyFor(Guid.NewGuid(), Guid.Empty));
    }

    [Fact]
    public void Restore_RebuildsTheSameReferenceFromKeyAndVersion()
    {
        var original = AvatarReference.For(Guid.NewGuid(), Guid.NewGuid(), 4);

        var restored = AvatarReference.Restore(original.Key, original.Version);

        Assert.Equal(original, restored);
    }
}