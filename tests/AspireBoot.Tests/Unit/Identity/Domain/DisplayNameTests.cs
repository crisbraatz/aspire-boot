using AspireBoot.Core.Identity.Domain;

namespace AspireBoot.Tests.Unit.Identity.Domain;

[Trait("Category", "Unit")]
public sealed class DisplayNameTests
{
    [Theory]
    [InlineData("Ada Lovelace", "Ada Lovelace")]
    [InlineData("  Grace Hopper  ", "Grace Hopper")]
    [InlineData("Zé", "Zé")]
    public void Create_WithValidValue_ReturnsTrimmedName(string input, string expected)
    {
        var result = DisplayName.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingValue_FailsWithEmpty(string? input)
    {
        var result = DisplayName.Create(input);

        Assert.False(result.IsSuccess);
        Assert.Equal(DisplayName.Empty, result.Failure);
    }

    [Fact]
    public void Create_WithSingleCharacter_FailsWithTooShort()
    {
        var result = DisplayName.Create("A");

        Assert.Equal(DisplayName.TooShort, result.Failure);
    }

    [Fact]
    public void Create_AboveMaxLength_FailsWithTooLong()
    {
        var result = DisplayName.Create(new string('a', DisplayName.MaxLength + 1));

        Assert.Equal(DisplayName.TooLong, result.Failure);
    }

    [Fact]
    public void Create_WithControlCharacters_FailsWithInvalidCharacters()
    {
        var result = DisplayName.Create("Ada\u0000Lovelace");

        Assert.Equal(DisplayName.InvalidCharacters, result.Failure);
    }

    [Fact]
    public void Value_OfFailedResult_Throws()
    {
        var result = DisplayName.Create(null);

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}