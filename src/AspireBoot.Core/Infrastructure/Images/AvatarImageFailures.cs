using AspireBoot.Core.Shared;

namespace AspireBoot.Core.Infrastructure.Images;

/// <summary>Stable failure codes for avatar uploads.</summary>
public static class AvatarImageFailures
{
    public static readonly Failure UnsupportedType = new("avatar.unsupportedType", "Avatar must be a JPEG, PNG or WebP image.");
    public static readonly Failure Corrupt = new("avatar.corrupt", "Avatar image is corrupt or cannot be decoded.");
    public static readonly Failure DimensionsTooLarge = new("avatar.dimensionsTooLarge", "Avatar image dimensions are too large.");
}