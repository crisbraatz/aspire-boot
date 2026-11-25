using JasperFx.CommandLine.Descriptions;
using JasperFx.Resources;

namespace AspireBoot.Core.Infrastructure.Storage;

/// <summary>
/// Exposes the bucket to JasperFx. <c>AddResourceSetupOnStartup</c> and <c>resources setup</c> only enumerate the stateful
/// resources reported by registered system parts, so a bare <see cref="IStatefulResource"/> in DI is never provisioned.
/// </summary>
public sealed class StorageSystemPart(StorageBucketResource bucket) : SystemPartBase("Object storage", new Uri("s3://aspireboot"))
{
    public override ValueTask<IReadOnlyList<IStatefulResource>> FindResources()
        => new([bucket]);
}