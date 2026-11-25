using AspireBoot.Core.Identity.Domain;

namespace AspireBoot.Tests.Unit.Identity.Domain;

[Trait("Category", "Unit")]
public sealed class RoleAssignmentTests
{
    private static readonly Guid ActorId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid OtherId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void EnsureCanChangeRole_WhenPromotingUserToAdmin_Succeeds()
    {
        var result = RoleAssignment.EnsureCanChangeRole(ActorId, OtherId, Roles.User, Roles.Admin, 1);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void EnsureCanChangeRole_WhenAdminDemotesAnotherAdmin_Succeeds()
    {
        var result = RoleAssignment.EnsureCanChangeRole(ActorId, OtherId, Roles.Admin, Roles.User, 2);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void EnsureCanChangeRole_WhenSameRole_SucceedsAsUnchanged()
    {
        var result = RoleAssignment.EnsureCanChangeRole(ActorId, ActorId, Roles.Admin, Roles.Admin, 1);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void EnsureCanChangeRole_WhenAdminDemotesSelf_FailsWithSelfDemotion()
    {
        var result = RoleAssignment.EnsureCanChangeRole(ActorId, ActorId, Roles.Admin, Roles.User, 2);

        Assert.False(result.IsSuccess);
        Assert.Equal(RoleAssignment.SelfDemotion, result.Failure);
    }

    [Fact]
    public void EnsureCanChangeRole_WhenDemotingTheOnlyAdmin_FailsWithLastAdmin()
    {
        var result = RoleAssignment.EnsureCanChangeRole(ActorId, OtherId, Roles.Admin, Roles.User, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(RoleAssignment.LastAdmin, result.Failure);
    }

    [Fact]
    public void EnsureCanChangeRole_WithUnknownNewRole_FailsWithInvalidRole()
    {
        var result = RoleAssignment.EnsureCanChangeRole(ActorId, OtherId, Roles.User, "Owner", 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(RoleAssignment.InvalidRole, result.Failure);
    }

    [Fact]
    public void Failures_UseStableCamelCaseCodes()
    {
        Assert.Equal("identity.selfDemotion", RoleAssignment.SelfDemotion.Code);
        Assert.Equal("identity.lastAdmin", RoleAssignment.LastAdmin.Code);
        Assert.Equal("identity.invalidRole", RoleAssignment.InvalidRole.Code);
    }

    [Fact]
    public void Roles_All_ListsBuiltInRoles()
    {
        Assert.Equal(new[] { Roles.Admin, Roles.User }, Roles.All);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("User", true)]
    [InlineData("admin", false)]
    [InlineData("Owner", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Roles_IsKnown_MatchesBuiltInNamesExactly(string? name, bool expected)
    {
        Assert.Equal(expected, Roles.IsKnown(name));
    }

    [Theory]
    [InlineData("admin@example.com", "admin@example.com")]
    [InlineData("ADMIN@Example.com", "admin@example.com")]
    [InlineData("  admin@example.com  ", "admin@example.com")]
    public void ShouldBootstrapAdmin_WhenConfiguredEmailMatches_ReturnsTrue(string configured, string userEmail)
    {
        Assert.True(RoleAssignment.ShouldBootstrapAdmin(configured, userEmail, emailConfirmed: true, alreadyAdmin: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldBootstrapAdmin_WhenNotConfigured_ReturnsFalse(string? configured)
    {
        Assert.False(RoleAssignment.ShouldBootstrapAdmin(configured, "admin@example.com", emailConfirmed: true, alreadyAdmin: false));
    }

    [Fact]
    public void ShouldBootstrapAdmin_WhenEmailNotConfirmed_ReturnsFalse()
    {
        Assert.False(RoleAssignment.ShouldBootstrapAdmin("admin@example.com", "admin@example.com", emailConfirmed: false, alreadyAdmin: false));
    }

    [Fact]
    public void ShouldBootstrapAdmin_WhenAlreadyAdmin_ReturnsFalse()
    {
        Assert.False(RoleAssignment.ShouldBootstrapAdmin("admin@example.com", "admin@example.com", emailConfirmed: true, alreadyAdmin: true));
    }

    [Fact]
    public void ShouldBootstrapAdmin_WhenEmailDiffers_ReturnsFalse()
    {
        Assert.False(RoleAssignment.ShouldBootstrapAdmin("admin@example.com", "someone@example.com", emailConfirmed: true, alreadyAdmin: false));
    }
}