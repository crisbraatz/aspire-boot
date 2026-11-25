using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AspireBoot.Tests.Unit.Api.Shared;

[Trait("Category", "Unit")]
public sealed class AuthorizationPoliciesTests
{
    [Fact]
    public async Task AdminPolicy_RequiresAuthenticatedUserWithTheAdminRole()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddApiSecurity();
        await using var provider = builder.Services.BuildServiceProvider();

        var policy = await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(AuthorizationPolicies.Admin);

        Assert.Equal("admin", AuthorizationPolicies.Admin);
        Assert.NotNull(policy);
        Assert.Contains(policy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        var roles = Assert.Single(policy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal([Roles.Admin], roles.AllowedRoles);
    }
}