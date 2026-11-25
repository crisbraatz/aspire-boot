using AspireBoot.Core.Identity.Domain;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspireBoot.Tests.Integration.Infrastructure;

/// <summary>
/// Hosts the API with a UserManager whose role assignment can be switched to report failure (an IdentityResult, not an
/// exception), so tests can prove that a failed write rolls the whole request back.
/// </summary>
public sealed class RoleFailureApiFactory(
    InfrastructureFixture infrastructure,
    IReadOnlyDictionary<string, string?>? settingOverrides = null) : AspireBootApiFactory(infrastructure, settingOverrides)
{
    private readonly RoleFailureSwitch _switch = new();

    /// <summary>While true, <c>AddToRoleAsync</c> returns a failed result without writing.</summary>
    public bool FailRoleAssignment
    {
        get => _switch.Armed;
        set => _switch.Armed = value;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(_switch);
            services.AddScoped<UserManager<User>, FailingUserManager>();
        });
    }

    private sealed class RoleFailureSwitch
    {
        public volatile bool Armed;
    }

    private sealed class FailingUserManager(
        IUserStore<User> store,
        IOptions<IdentityOptions> options,
        IPasswordHasher<User> passwordHasher,
        IEnumerable<IUserValidator<User>> userValidators,
        IEnumerable<IPasswordValidator<User>> passwordValidators,
        ILookupNormalizer normalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<UserManager<User>> logger,
        RoleFailureSwitch failure)
        : UserManager<User>(store, options, passwordHasher, userValidators, passwordValidators, normalizer, errors, services, logger)
    {
        public override Task<IdentityResult> AddToRoleAsync(User user, string role) =>
            failure.Armed
                ? Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "TestRoleFailure", Description = "Injected failure." }))
                : base.AddToRoleAsync(user, role);
    }
}