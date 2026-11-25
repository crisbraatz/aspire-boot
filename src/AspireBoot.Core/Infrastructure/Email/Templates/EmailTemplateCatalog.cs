using AspireBoot.Core.Identity.Domain;

namespace AspireBoot.Core.Infrastructure.Email.Templates;

/// <summary>The emails the platform sends. Each kind has a plain-text and an HTML body per culture.</summary>
public enum EmailTemplateKind
{
    ConfirmationEmail,
    RecoveryEmail,
    PasskeyAdded,
    PasskeyRemoved,
    RoleChanged,
}

/// <summary>Values interpolated into a template. Link is required for the kinds that carry one.</summary>
public sealed record EmailTemplateModel(string DisplayName, string? Link = null, string? PasskeyName = null, string? RoleName = null);

/// <summary>A rendered email. Subject and text are plain; the HTML body has every user value encoded.</summary>
public sealed record RenderedEmail(string Subject, string Text, string Html);

/// <summary>Renders localized emails. Unknown cultures fall back to <see cref="UiCulture.Default"/>.</summary>
public static class EmailTemplateCatalog
{
    public static RenderedEmail Render(EmailTemplateKind kind, string culture, EmailTemplateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var resolved = UiCulture.Create(culture);
        var uiCulture = resolved.IsSuccess ? resolved.Value : UiCulture.Default;

        return IdentityEmailTemplates.Render(kind, uiCulture, model);
    }
}