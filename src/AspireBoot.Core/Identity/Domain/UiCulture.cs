using AspireBoot.Core.Shared;

namespace AspireBoot.Core.Identity.Domain;

/// <summary>The interface language of a user. Only the cultures the UI is translated into are accepted.</summary>
public sealed record UiCulture
{
    public static readonly UiCulture PtBr = new("pt-BR");
    public static readonly UiCulture EnUs = new("en-US");
    public static readonly UiCulture EsEs = new("es-ES");

    public static readonly IReadOnlyList<UiCulture> Supported = [PtBr, EnUs, EsEs];

    public static readonly UiCulture Default = EnUs;

    public static readonly Failure Empty = new("identity.ui_culture.empty", "UI culture is required.");
    public static readonly Failure Unsupported = new("identity.ui_culture.unsupported", "UI culture is not supported.");

    private UiCulture(string value) => Value = value;

    /// <summary>The canonical BCP 47 tag, for example <c>pt-BR</c>.</summary>
    public string Value { get; }

    public static Result<UiCulture> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Fail<UiCulture>(Empty);
        }

        var match = Supported.FirstOrDefault(c => string.Equals(c.Value, value.Trim(), StringComparison.OrdinalIgnoreCase));

        return match is null
            ? Result.Fail<UiCulture>(Unsupported)
            : Result.Success(match);
    }

    public override string ToString() => Value;
}