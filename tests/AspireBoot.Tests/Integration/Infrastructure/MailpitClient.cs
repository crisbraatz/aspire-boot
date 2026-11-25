using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AspireBoot.Tests.Integration.Infrastructure;

/// <summary>Reads and clears the mail captured by the Mailpit container through its HTTP API.</summary>
public sealed class MailpitClient(HttpClient http)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Polls until a message addressed to <paramref name="recipient"/> arrives or the timeout expires.</summary>
    public async Task<MailpitMessage> WaitForMessageAsync(string recipient, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            while (true)
            {
                var summaries = await SearchAsync(recipient, timeoutCts.Token);
                if (summaries.Count > 0)
                {
                    return await GetMessageAsync(summaries[0].Id, timeoutCts.Token);
                }

                await Task.Delay(PollInterval, timeoutCts.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No email to '{recipient}' arrived in Mailpit within {timeout}.");
        }
    }

    public async Task<IReadOnlyList<MailpitMessageSummary>> SearchAsync(string recipient, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString($"to:{recipient}");
        var result = await http.GetFromJsonAsync<SearchResponse>($"api/v1/search?query={query}", cancellationToken);
        return result?.Messages ?? [];
    }

    public async Task<MailpitMessage> GetMessageAsync(string id, CancellationToken cancellationToken)
    {
        var message = await http.GetFromJsonAsync<MessageResponse>($"api/v1/message/{Uri.EscapeDataString(id)}", cancellationToken)
            ?? throw new InvalidOperationException($"Mailpit returned no body for message '{id}'.");
        return new MailpitMessage(message.Id, message.Subject, message.Text, message.Html);
    }

    /// <summary>Deletes every captured message so each test starts with an empty inbox.</summary>
    public async Task DeleteAllAsync(CancellationToken cancellationToken)
    {
        using var response = await http.DeleteAsync("api/v1/messages", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record SearchResponse(
        [property: JsonPropertyName("messages")] IReadOnlyList<MailpitMessageSummary> Messages);

    private sealed record MessageResponse(
        [property: JsonPropertyName("ID")] string Id,
        [property: JsonPropertyName("Subject")] string Subject,
        [property: JsonPropertyName("Text")] string Text,
        [property: JsonPropertyName("HTML")] string Html);
}

public sealed record MailpitMessageSummary([property: JsonPropertyName("ID")] string Id);

public sealed record MailpitMessage(string Id, string Subject, string Text, string Html);