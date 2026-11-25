using System.Net;

using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

using AspireBoot.Core.Infrastructure.Storage;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace AspireBoot.Tests.Integration.Infrastructure;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class AvatarStorageTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private readonly AspireBootApiFactory _factory = new(infrastructure);

    [Fact]
    public async Task StartingTheHost_ProvisionsTheAvatarBucket()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var scope = _factory.Services.CreateScope();
        var s3 = scope.ServiceProvider.GetRequiredService<IAmazonS3>();

        await s3.HeadBucketAsync(new HeadBucketRequest { BucketName = infrastructure.StorageBucket }, cancellationToken);
    }

    [Fact]
    public async Task StartingTheApiHost_WithAnEmptyBucketName_CreatesTheBucketWithoutAnExplicitSetup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bucket = $"startup-api-{Guid.NewGuid():N}";
        await using var api = new AspireBootApiFactory(infrastructure, new Dictionary<string, string?> { ["Storage:Bucket"] = bucket });

        _ = api.Services;

        using var scope = api.Services.CreateScope();
        var s3 = scope.ServiceProvider.GetRequiredService<IAmazonS3>();
        await s3.HeadBucketAsync(new HeadBucketRequest { BucketName = bucket }, cancellationToken);
    }

    [Fact]
    public async Task StartingTheWorkerHost_WithAnEmptyBucketName_CreatesTheBucketWithoutAnExplicitSetup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bucket = $"startup-worker-{Guid.NewGuid():N}";
        await using var worker = new AspireBootWorkerFactory(infrastructure)
            .WithWebHostBuilder(builder => builder.UseSetting("Storage:Bucket", bucket));

        _ = worker.Services;

        using var scope = worker.Services.CreateScope();
        var s3 = scope.ServiceProvider.GetRequiredService<IAmazonS3>();
        await s3.HeadBucketAsync(new HeadBucketRequest { BucketName = bucket }, cancellationToken);
    }

    [Fact]
    public async Task PutOpenReadDelete_RoundTripsTheObject()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = $"avatars/{Guid.NewGuid():N}/{Guid.NewGuid():N}.webp";
        var content = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x01, 0x02, 0x03 };

        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IAvatarStorage>();

        await storage.PutAsync(key, content, cancellationToken);

        await using (var stream = await storage.OpenReadAsync(key, cancellationToken))
        {
            Assert.NotNull(stream);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            Assert.Equal(content, buffer.ToArray());
        }

        await storage.DeleteAsync(key, cancellationToken);

        Assert.Null(await storage.OpenReadAsync(key, cancellationToken));
    }

    [Fact]
    public async Task OpenRead_MissingKey_ReturnsNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IAvatarStorage>();

        Assert.Null(await storage.OpenReadAsync($"avatars/missing/{Guid.NewGuid():N}.webp", cancellationToken));
    }

    [Fact]
    public async Task Delete_MissingKey_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IAvatarStorage>();

        await storage.DeleteAsync($"avatars/missing/{Guid.NewGuid():N}.webp", cancellationToken);
        await storage.DeleteAsync($"avatars/missing/{Guid.NewGuid():N}.webp", cancellationToken);
    }

    [Fact]
    public async Task BucketSetup_WhenBucketAlreadyExists_IsIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var scope = _factory.Services.CreateScope();
        var resource = scope.ServiceProvider.GetRequiredService<StorageBucketResource>();

        await resource.Setup(cancellationToken);
        await resource.Setup(cancellationToken);
        await resource.Check(cancellationToken);
    }

    [Fact]
    public async Task AnonymousGet_OnAnAvatarObject_IsForbidden()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = $"avatars/{Guid.NewGuid():N}/{Guid.NewGuid():N}.webp";

        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IAvatarStorage>();
            await storage.PutAsync(key, new byte[] { 0x52, 0x49, 0x46, 0x46, 0x01 }, cancellationToken);
        }

        // No credentials: the bucket must not be readable by anyone but the API and the Worker.
        using var anonymous = new AmazonS3Client(
            new AnonymousAWSCredentials(),
            new AmazonS3Config { ServiceURL = infrastructure.StorageServiceUrl, ForcePathStyle = true });

        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(() => anonymous.GetObjectAsync(
            new GetObjectRequest { BucketName = infrastructure.StorageBucket, Key = key },
            cancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);

        using var cleanup = _factory.Services.CreateScope();
        await cleanup.ServiceProvider.GetRequiredService<IAvatarStorage>().DeleteAsync(key, cancellationToken);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}