using AspireBoot.Core.Infrastructure.Email.Templates;
using AspireBoot.Core.Messaging.Identity;
using AspireBoot.Core.Persistence;
using AspireBoot.Tests.Integration.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

using Wolverine.EntityFrameworkCore;

namespace AspireBoot.Tests.Integration.Identity.Notifications;

/// <summary>Publishes each identity notification from the Api outbox and checks that the Worker sends the localized email.</summary>
[Trait("Category", "Integration")]
public sealed class NotificationEmailTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(30);

    private readonly AspireBootApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task ConfirmationEmailRequested_ArrivesLocalizedInPtBr()
    {
        var ct = TestContext.Current.CancellationToken;
        const string email = "confirm-ptbr@aspireboot.test";
        var message = new ConfirmationEmailRequested(Guid.NewGuid(), email, "Ana", "pt-BR", "https://localhost/confirm?u=1&t=abc");
        await infrastructure.ResetDatabaseAsync();

        await PublishAsync(message, ct);

        var expected = EmailTemplateCatalog.Render(EmailTemplateKind.ConfirmationEmail, "pt-BR",
            new EmailTemplateModel("Ana", message.Link));
        var received = await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct);
        Assert.Equal(expected.Subject, received.Subject);
        Assert.Contains(message.Link.Replace("&", "&amp;", StringComparison.Ordinal), received.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecoveryEmailRequested_ArrivesLocalizedInEsEs()
    {
        var ct = TestContext.Current.CancellationToken;
        const string email = "recovery-eses@aspireboot.test";
        var message = new RecoveryEmailRequested(Guid.NewGuid(), email, "Luis", "es-ES", "https://localhost/recover?u=1&t=xyz");
        await infrastructure.ResetDatabaseAsync();

        await PublishAsync(message, ct);

        var expected = EmailTemplateCatalog.Render(EmailTemplateKind.RecoveryEmail, "es-ES",
            new EmailTemplateModel("Luis", message.Link));
        var received = await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct);
        Assert.Equal(expected.Subject, received.Subject);
        Assert.Contains("Caduca en 2 horas", received.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasskeyAddedNotification_ArrivesLocalizedInEnUs()
    {
        var ct = TestContext.Current.CancellationToken;
        const string email = "added-enus@aspireboot.test";
        var message = new PasskeyAddedNotification(Guid.NewGuid(), email, "Ana", "en-US", "Laptop");
        await infrastructure.ResetDatabaseAsync();

        await PublishAsync(message, ct);

        var expected = EmailTemplateCatalog.Render(EmailTemplateKind.PasskeyAdded, "en-US",
            new EmailTemplateModel("Ana", PasskeyName: "Laptop"));
        var received = await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct);
        Assert.Equal(expected.Subject, received.Subject);
        Assert.Contains("Laptop", received.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasskeyRemovedNotification_ArrivesLocalizedInPtBr()
    {
        var ct = TestContext.Current.CancellationToken;
        const string email = "removed-ptbr@aspireboot.test";
        var message = new PasskeyRemovedNotification(Guid.NewGuid(), email, "Ana", "pt-BR", "Celular");
        await infrastructure.ResetDatabaseAsync();

        await PublishAsync(message, ct);

        var expected = EmailTemplateCatalog.Render(EmailTemplateKind.PasskeyRemoved, "pt-BR",
            new EmailTemplateModel("Ana", PasskeyName: "Celular"));
        var received = await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct);
        Assert.Equal(expected.Subject, received.Subject);
        Assert.Contains("Celular", received.Text, StringComparison.Ordinal);
    }

    /// <summary>Enlists the message in the Api's outbox and commits it, so Wolverine relays it to RabbitMQ.</summary>
    private async Task PublishAsync<T>(T message, CancellationToken cancellationToken)
    {
        using var scope = _api.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<AppDbContext>>();
        await outbox.PublishAsync(message);
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }

    public ValueTask InitializeAsync()
    {
        // Touching Services starts the Worker host, which then listens on identity-notifications.
        _ = _worker.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }
}