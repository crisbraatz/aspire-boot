using System.Net;

namespace AspireBoot.Tests.Integration.Infrastructure;

/// <summary>
/// Cookie-aware client for the Identity endpoints. It keeps the cookies the server sets, fetches the
/// antiforgery token from GET /api/antiforgery/token (the XSRF-TOKEN cookie, which is readable by
/// scripts) and sends the same value in the X-XSRF-TOKEN header on unsafe requests.
/// </summary>
public sealed class IdentityClient(AspireBootApiFactory factory) : IDisposable
{
    public const string AntiforgeryHeader = "X-XSRF-TOKEN";
    private const string TokenCookieName = "XSRF-TOKEN";

    private static readonly Uri TokenUri = new("/api/antiforgery/token", UriKind.Relative);

    // The framework's HTTP client keeps cookies between requests (HandleCookies is on by default).
    private readonly HttpClient _http = factory.CreateHttpsClient();

    private string? _antiforgeryToken;

    public HttpClient Http => _http;

    /// <summary>Requests a fresh antiforgery token and returns the value of the XSRF-TOKEN cookie.</summary>
    public async Task<string> RefreshAntiforgeryTokenAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(TokenUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        _antiforgeryToken = ReadTokenCookie(response)
            ?? throw new InvalidOperationException($"The response did not set the {TokenCookieName} cookie.");
        return _antiforgeryToken;
    }

    /// <summary>Sends an unsafe request with the current antiforgery token in the header.</summary>
    public async Task<HttpResponseMessage> SendUnsafeAsync(HttpMethod method, Uri uri, HttpContent? content, CancellationToken cancellationToken)
    {
        var token = _antiforgeryToken ?? await RefreshAntiforgeryTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Add(AntiforgeryHeader, token);
        return await _http.SendAsync(request, cancellationToken);
    }

    public void Dispose() => _http.Dispose();

    private static string? ReadTokenCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        var prefix = TokenCookieName + "=";
        foreach (var cookie in cookies)
        {
            if (!cookie.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var end = cookie.IndexOf(';', StringComparison.Ordinal);
            return WebUtility.UrlDecode(end < 0 ? cookie[prefix.Length..] : cookie[prefix.Length..end]);
        }

        return null;
    }
}