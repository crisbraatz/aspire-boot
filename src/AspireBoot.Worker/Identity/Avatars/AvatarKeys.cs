namespace AspireBoot.Worker.Identity.Avatars;

/// <summary>Checks and reads server-generated avatar keys (<c>avatars/{userId:N}/{objectId:N}.webp</c>).</summary>
internal static class AvatarKeys
{
    private const string Extension = ".webp";

    /// <summary>
    /// True when the key is exactly one object of the user: the prefix and extension match and the object part is a
    /// 32-digit GUID in "N" form, so no separator, dot segment or extra text can slip through.
    /// </summary>
    public static bool BelongsTo(string key, Guid userId)
    {
        var prefix = $"avatars/{userId:N}/";
        if (key.Length < prefix.Length + Extension.Length
            || !key.StartsWith(prefix, StringComparison.Ordinal)
            || !key.EndsWith(Extension, StringComparison.Ordinal))
        {
            return false;
        }

        var objectPart = key.AsSpan(prefix.Length, key.Length - prefix.Length - Extension.Length);
        return Guid.TryParseExact(objectPart, "N", out _);
    }

    /// <summary>The GUID part of the key, which is the only part of the key that gets logged.</summary>
    public static string ObjectIdOf(string key) => Path.GetFileNameWithoutExtension(key);
}