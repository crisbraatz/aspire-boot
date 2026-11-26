namespace AspireBoot.Core.Identity.Domain;

/// <summary>
/// A coarse, privacy-friendly summary of a user agent: the browser family and the operating system only.
/// Detection is plain ordered substring matching, so no regex runs over attacker-controlled input.
/// </summary>
public sealed record UserAgentSummary
{
    private const int MaxInputLength = 512;
    private const string UnknownToken = "unknown";

    private static readonly string[] KnownBrowsers = ["edge", "opera", "samsung", "firefox", "chrome", "safari", UnknownToken];
    private static readonly string[] KnownOperatingSystems = ["windows", "macos", "ios", "android", "linux", "chromeos", UnknownToken];

    private UserAgentSummary(string browser, string os)
    {
        Browser = browser;
        Os = os;
    }

    public string Browser { get; }

    public string Os { get; }

    /// <summary>The stored form, "browser|os", at most 64 characters.</summary>
    public string Value => $"{Browser}|{Os}";

    public static UserAgentSummary Unknown { get; } = new(UnknownToken, UnknownToken);

    /// <summary>Summarizes a raw user agent header. Never throws and never returns null.</summary>
    public static UserAgentSummary Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return Unknown;
        }

        var input = userAgent.Length > MaxInputLength ? userAgent[..MaxInputLength] : userAgent;

        return new UserAgentSummary(DetectBrowser(input), DetectOs(input));
    }

    /// <summary>Rebuilds a summary from its stored value. Unrecognized tokens become unknown.</summary>
    public static UserAgentSummary FromStored(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return Unknown;
        }

        var parts = stored.Split('|');
        if (parts.Length != 2)
        {
            return Unknown;
        }

        return new UserAgentSummary(
            Known(parts[0], KnownBrowsers),
            Known(parts[1], KnownOperatingSystems));
    }

    private static string Known(string token, string[] allowed) =>
        Array.IndexOf(allowed, token) >= 0 ? token : UnknownToken;

    private static string DetectBrowser(string input)
    {
        if (ContainsAny(input, "Edg/", "EdgA/", "EdgiOS/"))
        {
            return "edge";
        }

        if (input.Contains("OPR/", StringComparison.Ordinal))
        {
            return "opera";
        }

        if (input.Contains("SamsungBrowser/", StringComparison.Ordinal))
        {
            return "samsung";
        }

        if (ContainsAny(input, "Firefox/", "FxiOS/"))
        {
            return "firefox";
        }

        if (ContainsAny(input, "Chrome/", "CriOS/"))
        {
            return "chrome";
        }

        if (input.Contains("Version/", StringComparison.Ordinal) && input.Contains("Safari/", StringComparison.Ordinal))
        {
            return "safari";
        }

        return UnknownToken;
    }

    private static string DetectOs(string input)
    {
        if (input.Contains("Windows NT", StringComparison.Ordinal))
        {
            return "windows";
        }

        if (ContainsAny(input, "iPhone", "iPad", "iPod"))
        {
            return "ios";
        }

        if (input.Contains("Android", StringComparison.Ordinal))
        {
            return "android";
        }

        if (input.Contains("CrOS", StringComparison.Ordinal))
        {
            return "chromeos";
        }

        if (ContainsAny(input, "Mac OS X", "Macintosh"))
        {
            return "macos";
        }

        if (input.Contains("Linux", StringComparison.Ordinal))
        {
            return "linux";
        }

        return UnknownToken;
    }

    private static bool ContainsAny(string input, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (input.Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}