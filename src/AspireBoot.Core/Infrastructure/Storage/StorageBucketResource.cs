using System.Net;

using Amazon.S3;
using Amazon.S3.Model;

using JasperFx.Resources;

using Microsoft.Extensions.Options;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace AspireBoot.Core.Infrastructure.Storage;

/// <summary>Provisions the private avatar bucket. Runs from `resources setup` and at host startup.</summary>
public sealed class StorageBucketResource(IAmazonS3 s3, IOptions<StorageOptions> options) : IStatefulResource
{
    public string Type => "storage";

    public string Name => options.Value.Bucket;

    public Uri SubjectUri => new($"s3://{options.Value.Bucket}");

    public Uri ResourceUri => new($"s3://{options.Value.Bucket}");

    public async Task Setup(CancellationToken token)
    {
        if (await BucketExistsAsync(token))
        {
            return;
        }

        await s3.PutBucketAsync(new PutBucketRequest { BucketName = options.Value.Bucket }, token);
    }

    public Task Check(CancellationToken token)
        => s3.HeadBucketAsync(new HeadBucketRequest { BucketName = options.Value.Bucket }, token);

    public Task Teardown(CancellationToken token) => Task.CompletedTask;

    public Task ClearState(CancellationToken token) => Task.CompletedTask;

    public async Task<IRenderable> DetermineStatus(CancellationToken token)
        => new Text(await BucketExistsAsync(token) ? "bucket exists" : "bucket missing");

    private async Task<bool> BucketExistsAsync(CancellationToken token)
    {
        try
        {
            await s3.HeadBucketAsync(new HeadBucketRequest { BucketName = options.Value.Bucket }, token);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}