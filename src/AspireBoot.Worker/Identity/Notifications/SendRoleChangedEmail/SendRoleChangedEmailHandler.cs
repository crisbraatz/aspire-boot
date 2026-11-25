using AspireBoot.Core.Infrastructure.Email;
using AspireBoot.Core.Infrastructure.Email.Templates;
using AspireBoot.Core.Messaging.Identity;

using Microsoft.Extensions.Logging;

namespace AspireBoot.Worker.Identity.Notifications.SendRoleChangedEmail;

/// <summary>Sends the localized <c>role-changed</c> email, in the target's culture, through the Wolverine inbox.</summary>
public sealed class SendRoleChangedEmailHandler
{
    public static async Task Handle(RoleChangedNotification message, IEmailSender sender, ILogger<SendRoleChangedEmailHandler> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var email = EmailTemplateCatalog.Render(
            EmailTemplateKind.RoleChanged,
            message.Culture,
            new EmailTemplateModel(message.DisplayName, RoleName: message.NewRole));

        await sender.SendAsync(
            new EmailMessage(message.Email, message.DisplayName, email.Subject, email.Html, email.Text),
            cancellationToken);

        IdentityNotificationsLog.NotificationSent(logger, "role-changed", message.UserId);
    }
}