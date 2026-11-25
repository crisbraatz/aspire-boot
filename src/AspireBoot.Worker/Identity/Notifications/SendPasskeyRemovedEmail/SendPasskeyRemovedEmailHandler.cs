using AspireBoot.Core.Infrastructure.Email;
using AspireBoot.Core.Infrastructure.Email.Templates;
using AspireBoot.Core.Messaging.Identity;

using Microsoft.Extensions.Logging;

namespace AspireBoot.Worker.Identity.Notifications.SendPasskeyRemovedEmail;

/// <summary>Sends the localized <c>passkey-removed</c> email through the Wolverine inbox.</summary>
public sealed class SendPasskeyRemovedEmailHandler
{
    public static async Task Handle(PasskeyRemovedNotification message, IEmailSender sender, ILogger<SendPasskeyRemovedEmailHandler> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var email = EmailTemplateCatalog.Render(
            EmailTemplateKind.PasskeyRemoved,
            message.Culture,
            new EmailTemplateModel(message.DisplayName, PasskeyName: message.PasskeyName));

        await sender.SendAsync(
            new EmailMessage(message.Email, message.DisplayName, email.Subject, email.Html, email.Text),
            cancellationToken);

        IdentityNotificationsLog.NotificationSent(logger, "passkey-removed", message.UserId);
    }
}