using System.ComponentModel.DataAnnotations;

using AspireBoot.Api.Identity.Shared;

namespace AspireBoot.Tests.Unit.Api.Identity.Shared;

[Trait("Category", "Unit")]
public sealed class IdentityFlowOptionsTests
{
    [Fact]
    public void SecurityStampValidationSeconds_DefaultsTo60()
    {
        Assert.Equal(60, new IdentityFlowOptions().SecurityStampValidationSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(86_401)]
    public void SecurityStampValidationSeconds_OutsideOneSecondToOneDay_IsRejected(int seconds)
    {
        var options = new IdentityFlowOptions { SecurityStampValidationSeconds = seconds };

        Assert.False(Validator.TryValidateObject(options, new ValidationContext(options), null, validateAllProperties: true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(86_400)]
    public void SecurityStampValidationSeconds_WithinRange_IsAccepted(int seconds)
    {
        var options = new IdentityFlowOptions { SecurityStampValidationSeconds = seconds };

        Assert.True(Validator.TryValidateObject(options, new ValidationContext(options), null, validateAllProperties: true));
    }
}