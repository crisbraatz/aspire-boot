using System.ComponentModel.DataAnnotations;

using AspireBoot.Core.Infrastructure.Images;

using Microsoft.Extensions.Options;

namespace AspireBoot.Tests.Unit.Infrastructure.Images;

[Trait("Category", "Unit")]
public sealed class AvatarProcessingLimiterTests
{
    [Fact]
    public async Task WaitAsync_GrantsUpToTheConfiguredNumberOfSlots()
    {
        var limiter = Create(maxConcurrent: 2, waitSeconds: 10);

        Assert.True(await limiter.WaitAsync(CancellationToken.None));
        Assert.True(await limiter.WaitAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WaitAsync_ReturnsFalseWhenNoSlotFreesBeforeTheTimeout()
    {
        var limiter = Create(maxConcurrent: 2, waitSeconds: 1);
        Assert.True(await limiter.WaitAsync(CancellationToken.None));
        Assert.True(await limiter.WaitAsync(CancellationToken.None));

        var acquired = await limiter.WaitAsync(CancellationToken.None);

        Assert.False(acquired);
    }

    [Fact]
    public async Task Release_FreesASlotForAWaitingCaller()
    {
        var limiter = Create(maxConcurrent: 1, waitSeconds: 10);
        Assert.True(await limiter.WaitAsync(CancellationToken.None));
        var waiter = limiter.WaitAsync(CancellationToken.None);

        limiter.Release();

        Assert.True(await waiter);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void Options_ValidateMaxConcurrentProcessingBetweenOneAndSixtyFour(int value, bool valid)
    {
        var options = new AvatarOptions { MaxConcurrentProcessing = value };

        var isValid = Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true);

        Assert.Equal(valid, isValid);
    }

    private static AvatarProcessingLimiter Create(int maxConcurrent, int waitSeconds) =>
        new(Options.Create(new AvatarOptions { MaxConcurrentProcessing = maxConcurrent, ProcessingWaitSeconds = waitSeconds }));
}