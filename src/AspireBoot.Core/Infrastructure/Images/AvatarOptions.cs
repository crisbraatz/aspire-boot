using System.ComponentModel.DataAnnotations;

namespace AspireBoot.Core.Infrastructure.Images;

public sealed class AvatarOptions
{
    public const string SectionName = "Avatars";

    /// <summary>Upper bound on concurrent decodes. Memory is bounded by this value x 24 MP x 4 bytes.</summary>
    [Range(1, 64)]
    public int MaxConcurrentProcessing { get; init; } = 2;

    /// <summary>How long a request waits for a processing slot before it gets 503 avatar.busy.</summary>
    [Range(1, 60)]
    public int ProcessingWaitSeconds { get; init; } = 10;

    /// <summary>
    /// How long after an upload begins the Worker checks that the stored object is still wanted. Cleans objects left
    /// by a crash or a failed compensation. Keep it well above the longest upload; tests use a few seconds.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "7.00:00:00")]
    public TimeSpan OrphanCheckDelay { get; init; } = TimeSpan.FromHours(1);
}