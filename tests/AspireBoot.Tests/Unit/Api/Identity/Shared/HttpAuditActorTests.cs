using System.Security.Claims;

using AspireBoot.Api.Identity.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AspireBoot.Tests.Unit.Api.Identity.Shared;

[Trait("Category", "Unit")]
public sealed class HttpAuditActorTests
{
    private static readonly Guid UserId = Guid.Parse("0198b000-0000-7000-8000-000000000003");
    private static readonly Guid TargetId = Guid.Parse("0198b000-0000-7000-8000-000000000004");

    [Fact]
    public void ReadsTheClaimTypeConfiguredInIdentityOptions()
    {
        var options = new IdentityOptions();
        options.ClaimsIdentity.UserIdClaimType = "custom-user-id";
        var sut = Create(Principal("custom-user-id", UserId), options);

        Assert.Equal(UserId, sut.UserId);
    }

    [Fact]
    public void IgnoresTheDefaultClaimType_WhenAnotherOneIsConfigured()
    {
        var options = new IdentityOptions();
        options.ClaimsIdentity.UserIdClaimType = "custom-user-id";
        var sut = Create(Principal(ClaimTypes.NameIdentifier, UserId), options);

        Assert.Null(sut.UserId);
    }

    [Fact]
    public void AnonymousRequest_HasNoActor()
    {
        var sut = Create(new ClaimsPrincipal(new ClaimsIdentity()), new IdentityOptions());

        Assert.Null(sut.UserId);
    }

    [Fact]
    public void ActAs_OverridesTheActorUntilDisposed()
    {
        var sut = Create(new ClaimsPrincipal(new ClaimsIdentity()), new IdentityOptions());

        using (sut.ActAs(TargetId))
        {
            Assert.Equal(TargetId, sut.UserId);
        }

        Assert.Null(sut.UserId);
    }

    [Fact]
    public async Task ActAs_FlowsToAwaitedWork_ButNotToAnotherFlow()
    {
        var sut = Create(new ClaimsPrincipal(new ClaimsIdentity()), new IdentityOptions());

        var other = Task.Run(async () =>
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            return sut.UserId;
        }, TestContext.Current.CancellationToken);

        using (sut.ActAs(TargetId))
        {
            await Task.Yield();
            Assert.Equal(TargetId, sut.UserId);
        }

        Assert.Null(await other);
    }

    private static HttpAuditActor Create(ClaimsPrincipal principal, IdentityOptions options) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } }, Options.Create(options));

    private static ClaimsPrincipal Principal(string type, Guid id) =>
        new(new ClaimsIdentity([new Claim(type, id.ToString("D"))], "test"));
}