using System.Net;
using System.Threading.RateLimiting;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;
using AspireBoot.Core.Persistence.Auditing;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AspireBoot.Api.Shared;

internal static class SecurityExtensions
{
    public static WebApplicationBuilder AddApiSecurity(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        // Passwordless by design: accounts sign in with passkeys only. No password or lockout options exist.
        services.AddIdentityCore<User>(options =>
            {
                options.Stores.SchemaVersion = PersistenceExtensions.IdentitySchemaVersion;
                options.SignIn.RequireConfirmedEmail = true;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddTokenProvider<PasskeyRecoveryTokenProvider<User>>(PasskeyRecoveryTokenProvider<User>.ProviderName);

        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(2));

        services.AddOptions<PasskeyOptions>()
            .Bind(configuration.GetSection(PasskeyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<IdentityPasskeyOptions>()
            .Configure<IOptions<PasskeyOptions>>((identityPasskeys, passkeyOptions) =>
            {
                var passkeys = passkeyOptions.Value;
                var allowedOrigins = passkeys.AllowedOrigins.ToHashSet(StringComparer.Ordinal);

                identityPasskeys.ServerDomain = passkeys.ServerDomain;
                identityPasskeys.ResidentKeyRequirement = "required";
                identityPasskeys.ValidateOrigin = context => ValueTask.FromResult(allowedOrigins.Contains(context.Origin));
            });

        // Replaces the Core default (system). Singleton on purpose: it reads the accessor per call.
        services.AddHttpContextAccessor();
        services.AddSingleton<HttpAuditActor>();
        services.AddSingleton<IAuditActor>(provider => provider.GetRequiredService<HttpAuditActor>());
        services.AddSingleton<IAuditActorOverride>(provider => provider.GetRequiredService<HttpAuditActor>());

        services.AddOptions<IdentityFlowOptions>()
            .Bind(configuration.GetSection(IdentityFlowOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<BootstrapOptions>()
            .Bind(configuration.GetSection(BootstrapOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Immediate revocation (ADR 0005): every request compares the cookie stamp with the cached current stamp.
        services.AddScoped<ISecurityStampSource, DatabaseSecurityStampSource>();
        services.AddScoped<ISecurityStampCache, SecurityStampCache>();
        services.AddScoped<SecurityStampPrincipalValidator>();

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "__Host-aspireboot";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            // Replaces Identity's interval-based SecurityStampValidator.
            options.Events.OnValidatePrincipal = SecurityStampPrincipalValidator.OnValidatePrincipal;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // Secure by default: any endpoint without explicit metadata requires an authenticated user.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireAuthenticatedUser().RequireRole(Roles.Admin));

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "__Host-aspireboot-af";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        services.AddDataProtection()
            .SetApplicationName("AspireBoot")
            .PersistKeysToDbContext<AppDbContext>();

        services.AddApiRateLimiting(configuration);

        services.Configure<ForwardedHeadersOptions>(options =>
            ConfigureForwardedHeaders(options, configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? []));

        return builder;
    }

    /// <summary>Trusts X-Forwarded-For/Proto from the configured proxy networks only, one hop deep.</summary>
    internal static void ConfigureForwardedHeaders(ForwardedHeadersOptions options, IEnumerable<string> knownNetworks)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;

        foreach (var network in knownNetworks)
        {
            var parsed = System.Net.IPNetwork.Parse(network);
            if (parsed.PrefixLength == 0)
            {
                throw new ArgumentException($"ReverseProxy:KnownNetworks must not contain a wildcard network: {network}.", nameof(knownNetworks));
            }

            options.KnownIPNetworks.Add(parsed);
        }
    }
}