using MailKit.Net.Smtp;
using MailKit.Security;

using Microsoft.Extensions.Options;

using MimeKit;

namespace AspireBoot.Core.Infrastructure.Email;

internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;

        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        using var client = new SmtpClient();
        var security = settings.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        await client.ConnectAsync(settings.Host, settings.Port, security, cancellationToken);

        if (!string.IsNullOrEmpty(settings.Username))
        {
            await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(new SmtpSendRequest(mime), cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}