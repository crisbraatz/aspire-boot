using AspireBoot.Core.Infrastructure.Email;
using AspireBoot.Core.Infrastructure.Email.Templates;
using AspireBoot.Core.Messaging.Identity;

using Microsoft.Extensions.Logging;

namespace AspireBoot.Worker.Identity.Notifications.SendPasskeyAddedEmail;

/// <summary>Sends the localized <c>passkey-added</c> email through the Wolverine inbox.</summary>
public sealed class SendPasskeyAddedEmailHandler
{
    public static async Task Handle(PasskeyAddedNotification message, IEmailSender sender, ILogger<SendPasskeyAddedEmailHandler> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var email = EmailTemplateCatalog.Render(
            EmailTemplateKind.PasskeyAdded,
            message.Culture,
            new EmailTemplateModel(message.DisplayName, PasskeyName: message.PasskeyName));

        await sender.SendAsync(
            new EmailMessage(message.Email, message.DisplayName, email.Subject, email.Html, email.Text),
            cancellationToken);

        IdentityNotificationsLog.NotificationSent(logger, "passkey-added", message.UserId);
    }
}