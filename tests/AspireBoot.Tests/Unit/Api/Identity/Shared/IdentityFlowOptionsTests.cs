using System.ComponentModel.DataAnnotations;

using AspireBoot.Api.Identity.Shared;

namespace AspireBoot.Tests.Unit.Api.Identity.Shared;

[Trait("Category", "Unit")]
public sealed class IdentityFlowOptionsTests
{
    [Fact]
    public void EmailCooldownSeconds_DefaultsTo60()
    {
        Assert.Equal(60, new IdentityFlowOptions().EmailCooldownSeconds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3601)]
    public void EmailCooldownSeconds_OutsideRange_IsRejected(int seconds)
    {
        var options = new IdentityFlowOptions { EmailCooldownSeconds = seconds };

        Assert.False(Validator.TryValidateObject(options, new ValidationContext(options), null, validateAllProperties: true));
    }

    [Fact]
    public void IdentityFlowOptions_HasNoSecurityStampInterval()
    {
        Assert.Null(typeof(IdentityFlowOptions).GetProperty("SecurityStampValidationSeconds"));
    }
}