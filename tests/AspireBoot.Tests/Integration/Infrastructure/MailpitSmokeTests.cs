using AspireBoot.Core.Infrastructure.Email;

using Microsoft.Extensions.DependencyInjection;

namespace AspireBoot.Tests.Integration.Infrastructure;

[Trait("Category", "Integration")]
public sealed class MailpitSmokeTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private readonly AspireBootApiFactory _factory = new(infrastructure);

    [Fact]
    public async Task SendEmail_ThroughIEmailSender_CanBeReadBackFromMailpit()
    {
        await infrastructure.ResetDatabaseAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        using (var scope = _factory.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            await sender.SendAsync(
                new EmailMessage("ana@aspireboot.test", "Ana", "Smoke test", "<p>Olá, Ana</p>", "Olá, Ana"),
                cancellationToken);
        }

        var message = await infrastructure.Mailpit.WaitForMessageAsync(
            "ana@aspireboot.test", TimeSpan.FromSeconds(10), cancellationToken);

        Assert.Equal("Smoke test", message.Subject);
        Assert.Contains("Olá, Ana", message.Text, StringComparison.Ordinal);
        Assert.Contains("Olá, Ana", message.Html, StringComparison.Ordinal);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}