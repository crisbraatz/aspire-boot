using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AspireBoot.Tests.Integration.Infrastructure;

/// <summary>
/// Dedicated factory for expiry tests: identity tokens live for one second instead of two hours. The shared
/// <see cref="AspireBootApiFactory"/> keeps the production lifespan. The token provider reads the system clock,
/// so a short lifespan plus a real wait is the reliable way to expire a token.
/// </summary>
public sealed class AspireBootExpiringApiFactory(InfrastructureFixture infrastructure)
    : AspireBootApiFactory(infrastructure)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
            services.Configure<DataProtectionTokenProviderOptions>(options =>
                options.TokenLifespan = TimeSpan.FromSeconds(1)));
    }
}