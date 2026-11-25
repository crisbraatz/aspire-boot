using Microsoft.Extensions.Options;

namespace AspireBoot.Core.Infrastructure.Images;

/// <summary>Process-wide cap on concurrent avatar decodes, so memory stays bounded under parallel uploads.</summary>
public sealed class AvatarProcessingLimiter : IDisposable
{
    private readonly SemaphoreSlim _slots;
    private readonly TimeSpan _maxWait;

    public AvatarProcessingLimiter(IOptions<AvatarOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        _slots = new SemaphoreSlim(value.MaxConcurrentProcessing, value.MaxConcurrentProcessing);
        _maxWait = TimeSpan.FromSeconds(value.ProcessingWaitSeconds);
    }

    /// <summary>Waits for a slot. Returns false when none frees up within the configured wait.</summary>
    public Task<bool> WaitAsync(CancellationToken cancellationToken) =>
        _slots.WaitAsync(_maxWait, cancellationToken);

    /// <summary>Gives back a slot taken by a successful <see cref="WaitAsync"/>.</summary>
    public void Release() => _slots.Release();

    public void Dispose() => _slots.Dispose();
}