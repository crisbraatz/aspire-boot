extern alias WorkerApp;

using WorkerApp::AspireBoot.Worker.Identity.Avatars;

namespace AspireBoot.Tests.Unit.Identity.Avatars;

[Trait("Category", "Unit")]
public sealed class AvatarKeysTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string UserSegment = "11111111222233334444555555555555";
    private const string ObjectSegment = "aaaaaaaabbbbccccddddeeeeeeeeeeee";

    [Fact]
    public void BelongsTo_AcceptsTheOwnersServerGeneratedKey()
    {
        Assert.True(AvatarKeys.BelongsTo($"avatars/{UserSegment}/{ObjectSegment}.webp", UserId));
    }

    [Fact]
    public void BelongsTo_RejectsAnotherUsersKey()
    {
        Assert.False(AvatarKeys.BelongsTo($"avatars/{Guid.NewGuid():N}/{ObjectSegment}.webp", UserId));
    }

    [Theory]
    [InlineData("avatars/{U}/../{O}.webp")]
    [InlineData("avatars/{U}/{O}/../../x.webp")]
    [InlineData("avatars/{U}/{O}/x.webp")]
    [InlineData("avatars/{U}/x/{O}.webp")]
    [InlineData("avatars/{U}/..{O}.webp")]
    [InlineData("avatars/{U}/{O}.webp/x.webp")]
    [InlineData("avatars/{U}/{O}x.webp")]
    [InlineData("avatars/{U}/{O}.WEBP")]
    [InlineData("avatars/{U}/{O}.png")]
    [InlineData("avatars/{U}/{O}")]
    [InlineData("avatars/{U}/.webp")]
    [InlineData("avatars/{U}/not-a-guid.webp")]
    [InlineData("avatars/{U}/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.webp")]
    [InlineData("avatars/{U}/aaaaaaaabbbbccccddddeeeeeeeeeee.webp")]
    [InlineData("avatars/{U}/aaaaaaaabbbbccccddddeeeeeeeeeeeeff.webp")]
    [InlineData("avatars/{U}/{O}/")]
    [InlineData("avatars/{U}/")]
    [InlineData("avatars/{U}")]
    [InlineData("avatars/")]
    [InlineData("")]
    public void BelongsTo_RejectsKeysThatAreNotExactlyOneObjectOfTheUser(string template)
    {
        var key = template.Replace("{U}", UserSegment, StringComparison.Ordinal).Replace("{O}", ObjectSegment, StringComparison.Ordinal);

        Assert.False(AvatarKeys.BelongsTo(key, UserId));
    }

    [Fact]
    public void ObjectIdOf_ReturnsTheObjectSegment()
    {
        Assert.Equal(ObjectSegment, AvatarKeys.ObjectIdOf($"avatars/{UserSegment}/{ObjectSegment}.webp"));
    }
}