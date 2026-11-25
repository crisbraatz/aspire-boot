using System.Net;
using System.Security.Claims;

using AspireBoot.Api.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AspireBoot.Tests.Unit.Api.Shared;

[Trait("Category", "Unit")]
public sealed class RateLimitingTests
{
    [Fact]
    public void AvatarUploadPolicy_HasStableNameAndIsRegistered()
    {
        Assert.Equal("avatar-upload", RateLimitPolicies.AvatarUpload);
        Assert.Contains(RateLimitPolicies.AvatarUpload, RateLimitPolicies.All);
    }

    [Fact]
    public void GetPartitionKey_WhenAuthenticated_UsesUserId()
    {
        var userId = Guid.CreateVersion7().ToString();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));

        var key = RateLimitingExtensions.GetPartitionKey(user, IPAddress.Parse("203.0.113.9"));

        Assert.Equal($"user:{userId}", key);
    }

    [Fact]
    public void GetPartitionKey_WhenAnonymous_UsesRemoteIp()
    {
        var key = RateLimitingExtensions.GetPartitionKey(new ClaimsPrincipal(new ClaimsIdentity()), IPAddress.Parse("203.0.113.9"));

        Assert.Equal("ip:203.0.113.9", key);
    }

    [Fact]
    public void GetPartitionKey_WhenClaimPresentButNotAuthenticated_UsesRemoteIp()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "spoofed")]));

        var key = RateLimitingExtensions.GetPartitionKey(principal, IPAddress.Parse("203.0.113.9"));

        Assert.Equal("ip:203.0.113.9", key);
    }

    [Fact]
    public void GetPartitionKey_WhenRemoteIpUnknown_UsesUnknownBucket()
    {
        var key = RateLimitingExtensions.GetPartitionKey(null, null);

        Assert.Equal("ip:unknown", key);
    }

    [Fact]
    public async Task GetPartitionKey_UntrustedForwardedFor_DoesNotChangeKey()
    {
        var context = await ForwardHeadersAsync(remoteIp: "203.0.113.9", forwardedFor: "198.51.100.7");

        var key = RateLimitingExtensions.GetPartitionKey(null, context.Connection.RemoteIpAddress);

        Assert.Equal("ip:203.0.113.9", key);
    }

    [Fact]
    public async Task GetPartitionKey_TrustedForwardedFor_UsesClientAddress()
    {
        var context = await ForwardHeadersAsync(remoteIp: "10.0.0.1", forwardedFor: "198.51.100.7");

        var key = RateLimitingExtensions.GetPartitionKey(null, context.Connection.RemoteIpAddress);

        Assert.Equal("ip:198.51.100.7", key);
    }

    [Fact]
    public void ConfigureForwardedHeaders_WildcardNetwork_IsRejected()
    {
        var options = new ForwardedHeadersOptions();

        Assert.Throws<ArgumentException>(() => SecurityExtensions.ConfigureForwardedHeaders(options, ["0.0.0.0/0"]));
    }

    private static async Task<HttpContext> ForwardHeadersAsync(string remoteIp, string forwardedFor)
    {
        var options = new ForwardedHeadersOptions();
        SecurityExtensions.ConfigureForwardedHeaders(options, ["10.0.0.0/8"]);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;

        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
        await middleware.Invoke(context);

        return context;
    }
}