using System.ComponentModel.DataAnnotations;

namespace AspireBoot.Core.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required, Url]
    public string ServiceUrl { get; init; } = string.Empty;

    [Required]
    public string AccessKey { get; init; } = string.Empty;

    [Required]
    public string SecretKey { get; init; } = string.Empty;

    [Required]
    public string Bucket { get; init; } = "aspireboot";
}