using System.Net;

using Amazon.S3;
using Amazon.S3.Model;

using Microsoft.Extensions.Options;

namespace AspireBoot.Core.Infrastructure.Storage;

public sealed class S3AvatarStorage(IAmazonS3 s3, IOptions<StorageOptions> options) : IAvatarStorage
{
    private const string ContentType = "image/webp";

    public async Task PutAsync(string key, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        using var body = new MemoryStream(content.ToArray(), writable: false);
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = options.Value.Bucket,
            Key = key,
            ContentType = ContentType,
            InputStream = body,
        }, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        try
        {
            var response = await s3.GetObjectAsync(options.Value.Bucket, key, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        // S3 answers 204 for missing keys, so the delete is idempotent by protocol.
        await s3.DeleteObjectAsync(options.Value.Bucket, key, cancellationToken);
    }
}