using AspireBoot.Core.Shared;

namespace AspireBoot.Core.Identity.Domain;

/// <summary>The name a user shows to others. Trimmed, 2 to 100 characters, no control characters.</summary>
public sealed record DisplayName
{
    public const int MinLength = 2;
    public const int MaxLength = 100;

    public static readonly Failure Empty = new("identity.display_name.empty", "Display name is required.");
    public static readonly Failure TooShort = new("identity.display_name.too_short", $"Display name must have at least {MinLength} characters.");
    public static readonly Failure TooLong = new("identity.display_name.too_long", $"Display name must have at most {MaxLength} characters.");
    public static readonly Failure InvalidCharacters = new("identity.display_name.invalid_characters", "Display name contains invalid characters.");

    private DisplayName(string value) => Value = value;

    public string Value { get; }

    public static Result<DisplayName> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Fail<DisplayName>(Empty);
        }

        var trimmed = value.Trim();

        if (trimmed.Any(char.IsControl))
        {
            return Result.Fail<DisplayName>(InvalidCharacters);
        }

        if (trimmed.Length < MinLength)
        {
            return Result.Fail<DisplayName>(TooShort);
        }

        return trimmed.Length > MaxLength
            ? Result.Fail<DisplayName>(TooLong)
            : Result.Success(new DisplayName(trimmed));
    }

    public override string ToString() => Value;
}