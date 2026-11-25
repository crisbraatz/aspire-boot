using AspireBoot.Core.Identity.Domain;

namespace AspireBoot.Tests.Unit.Identity.Domain;

[Trait("Category", "Unit")]
public sealed class UiCultureTests
{
    [Fact]
    public void Supported_ContainsExactlyThePlatformCultures()
    {
        Assert.Equal(["pt-BR", "en-US", "es-ES"], UiCulture.Supported.Select(c => c.Value));
    }

    [Fact]
    public void Default_IsEnUs()
    {
        Assert.Equal("en-US", UiCulture.Default.Value);
    }

    [Theory]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt-br", "pt-BR")]
    [InlineData("  EN-us ", "en-US")]
    [InlineData("es-es", "es-ES")]
    public void Create_WithSupportedCulture_ReturnsCanonicalForm(string input, string expected)
    {
        var result = UiCulture.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("en")]
    [InlineData("pt-PT")]
    public void Create_WithUnsupportedCulture_FailsWithUnsupported(string input)
    {
        var result = UiCulture.Create(input);

        Assert.False(result.IsSuccess);
        Assert.Equal(UiCulture.Unsupported, result.Failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingValue_FailsWithEmpty(string? input)
    {
        var result = UiCulture.Create(input);

        Assert.Equal(UiCulture.Empty, result.Failure);
    }
}