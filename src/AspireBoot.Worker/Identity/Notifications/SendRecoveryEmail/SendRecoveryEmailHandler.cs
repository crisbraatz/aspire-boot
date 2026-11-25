using AspireBoot.Core.Infrastructure.Email;
using AspireBoot.Core.Infrastructure.Email.Templates;
using AspireBoot.Core.Messaging.Identity;

using Microsoft.Extensions.Logging;

namespace AspireBoot.Worker.Identity.Notifications.SendRecoveryEmail;

/// <summary>Sends the localized <c>recovery</c> email through the Wolverine inbox.</summary>
public sealed class SendRecoveryEmailHandler
{
    public static async Task Handle(RecoveryEmailRequested message, IEmailSender sender, ILogger<SendRecoveryEmailHandler> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var email = EmailTemplateCatalog.Render(
            EmailTemplateKind.RecoveryEmail,
            message.Culture,
            new EmailTemplateModel(message.DisplayName, message.Link));

        await sender.SendAsync(
            new EmailMessage(message.Email, message.DisplayName, email.Subject, email.Html, email.Text),
            cancellationToken);

        IdentityNotificationsLog.NotificationSent(logger, "recovery", message.UserId);
    }
}