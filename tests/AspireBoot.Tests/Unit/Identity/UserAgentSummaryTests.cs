using AspireBoot.Core.Identity.Domain;

namespace AspireBoot.Tests.Unit.Identity;

[Trait("Category", "Unit")]
public sealed class UserAgentSummaryTests
{
    private const string ChromeWindows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36", "chrome|windows")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0", "edge|windows")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0", "edge|macos")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36 EdgA/120.0.0.0", "edge|android")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) EdgiOS/120.0.0.0 Version/17.0 Mobile/15E148 Safari/604.1", "edge|ios")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 OPR/106.0.0.0", "opera|windows")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36 OPR/80.0.0.0", "opera|android")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SAMSUNG SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/23.0 Chrome/115.0.0.0 Mobile Safari/537.36", "samsung|android")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:121.0) Gecko/20100101 Firefox/121.0", "firefox|linux")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:121.0) Gecko/20100101 Firefox/121.0", "firefox|windows")]
    [InlineData("Mozilla/5.0 (Android 14; Mobile; rv:121.0) Gecko/121.0 Firefox/121.0", "firefox|android")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) FxiOS/120.0 Mobile/15E148 Safari/605.1.15", "firefox|ios")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/120.0.6099.119 Mobile/15E148 Safari/604.1", "chrome|ios")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/120.0.6099.119 Mobile/15E148 Safari/604.1", "chrome|ios")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1", "safari|ios")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1", "safari|ios")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.1 Safari/605.1.15", "safari|macos")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36", "chrome|macos")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36", "chrome|android")]
    [InlineData("Mozilla/5.0 (X11; CrOS x86_64 14541.0.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36", "chrome|chromeos")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36", "chrome|linux")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64)", "unknown|windows")]
    [InlineData("Windows NT 10.0", "unknown|windows")]
    [InlineData("curl/8.4.0", "unknown|unknown")]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", "unknown|unknown")]
    [InlineData("hello world", "unknown|unknown")]
    [InlineData("", "unknown|unknown")]
    [InlineData("   ", "unknown|unknown")]
    public void Parse_DetectsBrowserAndOs(string userAgent, string expected)
    {
        Assert.Equal(expected, UserAgentSummary.Parse(userAgent).Value);
    }

    [Fact]
    public void Parse_WithNull_ReturnsUnknown()
    {
        Assert.Equal("unknown|unknown", UserAgentSummary.Parse(null).Value);
    }

    [Fact]
    public void Parse_WithGarbageOfTenThousandChars_ReturnsUnknown()
    {
        var garbage = new string('x', 10_000);

        Assert.Equal("unknown|unknown", UserAgentSummary.Parse(garbage).Value);
    }

    [Fact]
    public void Parse_WithKnownTokensAfterTheFirst512Chars_IgnoresThem()
    {
        var userAgent = new string('a', 600) + " Chrome/120.0 Windows NT 10.0";

        Assert.Equal("unknown|unknown", UserAgentSummary.Parse(userAgent).Value);
    }

    [Fact]
    public void Parse_WithKnownTokensBeforeTheCutAndGarbageAfterIt_StillDetects()
    {
        var userAgent = "Mozilla/5.0 (X11; Linux x86_64) Chrome/120.0 Safari/537.36 " + new string('x', 10_000);

        Assert.Equal("chrome|linux", UserAgentSummary.Parse(userAgent).Value);
    }

    [Fact]
    public void Parse_EdgeWinsOverChromeInTheSameString()
    {
        Assert.Equal("edge|windows", UserAgentSummary.Parse(ChromeWindows + " Edg/120.0.0.0").Value);
    }

    [Fact]
    public void Parse_OperaWinsOverChromeInTheSameString()
    {
        Assert.Equal("opera|windows", UserAgentSummary.Parse(ChromeWindows + " OPR/106.0.0.0").Value);
    }

    [Fact]
    public void Parse_IsCaseSensitiveForTokens()
    {
        Assert.Equal("unknown|windows", UserAgentSummary.Parse("Windows NT 10.0 chrome/120").Value);
    }

    [Fact]
    public void Parse_IpadSafariIsSafariOnIos()
    {
        var summary = UserAgentSummary.Parse("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1");

        Assert.Equal("safari", summary.Browser);
        Assert.Equal("ios", summary.Os);
    }

    [Fact]
    public void FromStored_ParsesTheStoredValue()
    {
        var summary = UserAgentSummary.FromStored("edge|ios");

        Assert.Equal("edge", summary.Browser);
        Assert.Equal("ios", summary.Os);
        Assert.Equal("edge|ios", summary.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("chrome")]
    [InlineData("chrome|windows|extra")]
    [InlineData("garbage")]
    public void FromStored_WithBadShape_ReturnsUnknown(string? stored)
    {
        Assert.Equal("unknown|unknown", UserAgentSummary.FromStored(stored).Value);
    }

    [Fact]
    public void FromStored_WithUnknownTokens_ReplacesOnlyThoseTokens()
    {
        var summary = UserAgentSummary.FromStored("bogus|windows");

        Assert.Equal("unknown", summary.Browser);
        Assert.Equal("windows", summary.Os);
    }

    [Fact]
    public void Unknown_IsTheUnknownPair()
    {
        Assert.Equal("unknown|unknown", UserAgentSummary.Unknown.Value);
    }
}