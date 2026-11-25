using System.Globalization;
using System.Net;

using AspireBoot.Core.Identity.Domain;

namespace AspireBoot.Core.Infrastructure.Email.Templates;

/// <summary>Copy for the identity emails. Placeholders: {0} display name, {1} passkey name.</summary>
internal static class IdentityEmailTemplates
{
    private sealed record Copy(string Subject, string Greeting, string Body, string? ActionLabel, string Footer);

    private static readonly Dictionary<string, Dictionary<EmailTemplateKind, Copy>> Catalogs =
        new(StringComparer.Ordinal)
        {
            [UiCulture.PtBr.Value] = new Dictionary<EmailTemplateKind, Copy>
            {
                [EmailTemplateKind.ConfirmationEmail] = new(
                    "Confirme sua conta no AspireBoot",
                    "Olá, {0}!",
                    "Confirme seu e-mail para concluir a criação da sua conta:",
                    "Confirmar e-mail",
                    "Se você não criou uma conta, ignore esta mensagem."),
                [EmailTemplateKind.RecoveryEmail] = new(
                    "Recupere o acesso à sua conta no AspireBoot",
                    "Olá, {0}!",
                    "Use este link para recuperar o acesso e cadastrar uma nova chave de acesso. Ele expira em 2 horas:",
                    "Recuperar acesso",
                    "Se você não pediu isso, ignore esta mensagem."),
                [EmailTemplateKind.PasskeyAdded] = new(
                    "Uma chave de acesso foi adicionada à sua conta no AspireBoot",
                    "Olá, {0}!",
                    "A chave de acesso \"{1}\" foi adicionada à sua conta. Se não foi você, recupere sua conta imediatamente.",
                    null,
                    "Esta é uma notificação automática de segurança."),
                [EmailTemplateKind.PasskeyRemoved] = new(
                    "Uma chave de acesso foi removida da sua conta no AspireBoot",
                    "Olá, {0}!",
                    "A chave de acesso \"{1}\" foi removida da sua conta. Se não foi você, recupere sua conta imediatamente.",
                    null,
                    "Esta é uma notificação automática de segurança."),
            },
            [UiCulture.EnUs.Value] = new Dictionary<EmailTemplateKind, Copy>
            {
                [EmailTemplateKind.ConfirmationEmail] = new(
                    "Confirm your AspireBoot account",
                    "Hi {0},",
                    "Confirm your email to finish creating your account:",
                    "Confirm email",
                    "If you did not sign up, you can ignore this message."),
                [EmailTemplateKind.RecoveryEmail] = new(
                    "Recover access to your AspireBoot account",
                    "Hi {0},",
                    "Use this link to recover access and register a new passkey. It expires in 2 hours:",
                    "Recover access",
                    "If you did not request this, you can ignore this message."),
                [EmailTemplateKind.PasskeyAdded] = new(
                    "A passkey was added to your AspireBoot account",
                    "Hi {0},",
                    "The passkey \"{1}\" was added to your account. If this was not you, recover your account right away.",
                    null,
                    "This is an automatic security notification."),
                [EmailTemplateKind.PasskeyRemoved] = new(
                    "A passkey was removed from your AspireBoot account",
                    "Hi {0},",
                    "The passkey \"{1}\" was removed from your account. If this was not you, recover your account right away.",
                    null,
                    "This is an automatic security notification."),
            },
            [UiCulture.EsEs.Value] = new Dictionary<EmailTemplateKind, Copy>
            {
                [EmailTemplateKind.ConfirmationEmail] = new(
                    "Confirma tu cuenta de AspireBoot",
                    "¡Hola, {0}!",
                    "Confirma tu correo para terminar de crear tu cuenta:",
                    "Confirmar correo",
                    "Si no creaste una cuenta, puedes ignorar este mensaje."),
                [EmailTemplateKind.RecoveryEmail] = new(
                    "Recupera el acceso a tu cuenta de AspireBoot",
                    "¡Hola, {0}!",
                    "Usa este enlace para recuperar el acceso y registrar una nueva clave de acceso. Caduca en 2 horas:",
                    "Recuperar acceso",
                    "Si no lo pediste, puedes ignorar este mensaje."),
                [EmailTemplateKind.PasskeyAdded] = new(
                    "Se añadió una clave de acceso a tu cuenta de AspireBoot",
                    "¡Hola, {0}!",
                    "La clave de acceso \"{1}\" se añadió a tu cuenta. Si no fuiste tú, recupera tu cuenta de inmediato.",
                    null,
                    "Esta es una notificación automática de seguridad."),
                [EmailTemplateKind.PasskeyRemoved] = new(
                    "Se eliminó una clave de acceso de tu cuenta de AspireBoot",
                    "¡Hola, {0}!",
                    "La clave de acceso \"{1}\" se eliminó de tu cuenta. Si no fuiste tú, recupera tu cuenta de inmediato.",
                    null,
                    "Esta es una notificación automática de seguridad."),
            },
        };

    internal static RenderedEmail Render(EmailTemplateKind kind, UiCulture culture, EmailTemplateModel model)
    {
        var copy = Catalogs[culture.Value][kind];
        var needsLink = copy.ActionLabel is not null;
        if (needsLink && string.IsNullOrWhiteSpace(model.Link))
        {
            throw new ArgumentException("This email kind requires a link.", nameof(model));
        }

        var values = new[] { model.DisplayName, model.PasskeyName ?? string.Empty };
        var encoded = values.Select(value => WebUtility.HtmlEncode(value) ?? string.Empty).ToArray();

        var greeting = Format(copy.Greeting, values);
        var body = Format(copy.Body, values);
        var textLink = needsLink ? $"\n\n{copy.ActionLabel}: {model.Link}" : string.Empty;
        var text = $"{greeting}\n\n{body}{textLink}\n\n{copy.Footer}";

        var htmlAction = needsLink
            ? $"<p><a href=\"{WebUtility.HtmlEncode(model.Link)}\">{WebUtility.HtmlEncode(copy.ActionLabel)}</a></p>"
            : string.Empty;
        var html = $"<!doctype html><html lang=\"{culture.Value}\"><body>"
            + $"<p>{Format(copy.Greeting, encoded)}</p>"
            + $"<p>{Format(copy.Body, encoded)}</p>"
            + htmlAction
            + $"<p>{WebUtility.HtmlEncode(copy.Footer)}</p>"
            + "</body></html>";

        return new RenderedEmail(copy.Subject, text, html);
    }

    private static string Format(string template, string[] values) =>
        string.Format(CultureInfo.InvariantCulture, template, values);
}