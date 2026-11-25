using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Infrastructure.Email.Templates;

namespace AspireBoot.Tests.Unit.Infrastructure.Email;

[Trait("Category", "Unit")]
public sealed class IdentityEmailTemplatesTests
{
    private const string Link = "https://localhost/confirm?u=1&t=abc";

    private static readonly string[] Cultures = ["pt-BR", "en-US", "es-ES"];

    [Fact]
    public void Render_Confirmation_HasADifferentSubjectInEachCulture()
    {
        var model = new EmailTemplateModel("Ana", Link);

        var subjects = Cultures
            .Select(culture => EmailTemplateCatalog.Render(EmailTemplateKind.ConfirmationEmail, culture, model).Subject)
            .ToList();

        Assert.Equal(3, subjects.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    public void Render_Confirmation_ContainsTheLinkInTextAndHtml(string culture)
    {
        var model = new EmailTemplateModel("Ana", Link);

        var email = EmailTemplateCatalog.Render(EmailTemplateKind.ConfirmationEmail, culture, model);

        Assert.Contains(Link.Replace("&", "&amp;", StringComparison.Ordinal), email.Html, StringComparison.Ordinal);
        Assert.Contains(Link, email.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    public void Render_Recovery_ContainsTheLinkInTextAndHtml(string culture)
    {
        var model = new EmailTemplateModel("Ana", Link);

        var email = EmailTemplateCatalog.Render(EmailTemplateKind.RecoveryEmail, culture, model);

        Assert.Contains(Link.Replace("&", "&amp;", StringComparison.Ordinal), email.Html, StringComparison.Ordinal);
        Assert.Contains(Link, email.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithMarkupInDisplayName_EncodesItInHtmlOnly()
    {
        const string name = "<script>alert(\"Ana\") & co</script>";
        var model = new EmailTemplateModel(name, Link);

        var email = EmailTemplateCatalog.Render(EmailTemplateKind.ConfirmationEmail, "en-US", model);

        Assert.DoesNotContain("<script>", email.Html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(&quot;Ana&quot;) &amp; co&lt;/script&gt;", email.Html, StringComparison.Ordinal);
        Assert.Contains(name, email.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    public void Render_PasskeyAdded_IncludesThePasskeyNameAndNoLink(string culture)
    {
        var model = new EmailTemplateModel("Ana", PasskeyName: "Laptop");

        var email = EmailTemplateCatalog.Render(EmailTemplateKind.PasskeyAdded, culture, model);

        Assert.Contains("Laptop", email.Text, StringComparison.Ordinal);
        Assert.Contains("Laptop", email.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("href", email.Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    public void Render_PasskeyRemoved_IncludesThePasskeyNameAndNoLink(string culture)
    {
        var model = new EmailTemplateModel("Ana", PasskeyName: "Phone");

        var email = EmailTemplateCatalog.Render(EmailTemplateKind.PasskeyRemoved, culture, model);

        Assert.Contains("Phone", email.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("href", email.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_PasskeyRemoved_HasADifferentSubjectThanPasskeyAdded()
    {
        var model = new EmailTemplateModel("Ana", PasskeyName: "Phone");

        var added = EmailTemplateCatalog.Render(EmailTemplateKind.PasskeyAdded, "en-US", model);
        var removed = EmailTemplateCatalog.Render(EmailTemplateKind.PasskeyRemoved, "en-US", model);

        Assert.NotEqual(added.Subject, removed.Subject);
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("")]
    [InlineData("xx-XX")]
    public void Render_UnsupportedCulture_FallsBackToEnUs(string culture)
    {
        var model = new EmailTemplateModel("Ana", Link);

        var fallback = EmailTemplateCatalog.Render(EmailTemplateKind.ConfirmationEmail, culture, model);
        var english = EmailTemplateCatalog.Render(EmailTemplateKind.ConfirmationEmail, "en-US", model);

        Assert.Equal(english, fallback);
    }

    [Fact]
    public void Render_ConfirmationWithoutLink_Throws()
    {
        var model = new EmailTemplateModel("Ana");

        Assert.Throws<ArgumentException>(() =>
            EmailTemplateCatalog.Render(EmailTemplateKind.ConfirmationEmail, "en-US", model));
    }

    [Theory]
    [InlineData("pt-BR", "Administrador")]
    [InlineData("en-US", "Admin")]
    [InlineData("es-ES", "Administrador")]
    public void Render_RoleChanged_IncludesTheLocalizedRoleAndNoLink(string culture, string expectedRole)
    {
        var model = new EmailTemplateModel("Ana", RoleName: "Admin");

        var email = EmailTemplateCatalog.Render(EmailTemplateKind.RoleChanged, culture, model);

        Assert.Contains(expectedRole, email.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("href", email.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_RoleChanged_HasADistinctSubjectPerCulture()
    {
        var model = new EmailTemplateModel("Ana", RoleName: "User");

        var subjects = UiCulture.Supported.Select(culture => culture.Value)
            .Select(culture => EmailTemplateCatalog.Render(EmailTemplateKind.RoleChanged, culture, model).Subject)
            .Distinct(StringComparer.Ordinal);

        Assert.Equal(3, subjects.Count());
    }
}