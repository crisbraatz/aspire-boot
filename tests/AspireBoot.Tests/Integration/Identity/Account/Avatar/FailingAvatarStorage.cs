using AspireBoot.Core.Infrastructure.Storage;
using AspireBoot.Tests.Integration.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AspireBoot.Tests.Integration.Identity.Account.Avatar;

/// <summary>Storage whose writes always fail, to prove the profile stays untouched when the object cannot be stored.</summary>
internal sealed class FailingAvatarStorage : IAvatarStorage
{
    public Task PutAsync(string key, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Simulated storage outage.");

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) => Task.FromResult<Stream?>(null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// API host with <see cref="FailingAvatarStorage"/>. The real client is not pointed at a closed port because the
/// bucket is provisioned when the host starts; the real storage still provisions it, and only the upload port is replaced.
/// </summary>
internal sealed class FailingStorageApiFactory(InfrastructureFixture infrastructure, IReadOnlyDictionary<string, string?>? settings = null)
    : AspireBootApiFactory(infrastructure, settings)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            // Resolved by the endpoint only, after the host (and the bucket setup, which uses IAmazonS3) has started.
            services.AddSingleton<IAvatarStorage, FailingAvatarStorage>();
        });
    }
}