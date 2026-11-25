namespace AspireBoot.Core.Infrastructure.Storage;

/// <summary>Private object storage for processed avatar images. Keys are server-generated.</summary>
public interface IAvatarStorage
{
    /// <summary>Stores the object as image/webp, replacing any existing object with the same key.</summary>
    Task PutAsync(string key, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    /// <summary>Opens the object for reading, or returns null when the key does not exist.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Deletes the object. Deleting a missing key succeeds.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}