namespace AspireBoot.Core.Infrastructure.Email;

public sealed record EmailMessage(string ToAddress, string ToName, string Subject, string HtmlBody, string TextBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}