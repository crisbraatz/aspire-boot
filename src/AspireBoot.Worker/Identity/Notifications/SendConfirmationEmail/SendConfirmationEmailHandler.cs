using AspireBoot.Core.Infrastructure.Email;
using AspireBoot.Core.Infrastructure.Email.Templates;
using AspireBoot.Core.Messaging.Identity;

using Microsoft.Extensions.Logging;

namespace AspireBoot.Worker.Identity.Notifications.SendConfirmationEmail;

/// <summary>Sends the localized <c>confirmation</c> email through the Wolverine inbox.</summary>
public sealed class SendConfirmationEmailHandler
{
    public static async Task Handle(ConfirmationEmailRequested message, IEmailSender sender, ILogger<SendConfirmationEmailHandler> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var email = EmailTemplateCatalog.Render(
            EmailTemplateKind.ConfirmationEmail,
            message.Culture,
            new EmailTemplateModel(message.DisplayName, message.Link));

        await sender.SendAsync(
            new EmailMessage(message.Email, message.DisplayName, email.Subject, email.Html, email.Text),
            cancellationToken);

        IdentityNotificationsLog.NotificationSent(logger, "confirmation", message.UserId);
    }
}