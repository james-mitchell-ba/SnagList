namespace SnagList.Infrastructure.Tests.Email;

using System.Net.Http.Json;
using SnagList.Infrastructure.Email;
using SnagList.Infrastructure.Tests.Testing;
using Xunit;

[Collection("Mailpit")]
public class SmtpEmailSenderTests(MailpitFixture fixture)
{
    private sealed record MailpitMessageSummary(string Subject);
    private sealed record MailpitMessagesResponse(List<MailpitMessageSummary> Messages);

    [Fact]
    public async Task SendAsync_delivers_a_message_Mailpit_receives()
    {
        var sender = new SmtpEmailSender(new SmtpEmailSenderOptions
        {
            Host = fixture.Hostname,
            Port = fixture.SmtpPort,
            FromAddress = "snaglist@example.com",
            UseTls = false,
        });

        await sender.SendAsync(
            "maintenance@example.com", "New Snag reported", "A Snag was reported at Head Office.", default);

        using var httpClient = new HttpClient { BaseAddress = new Uri(fixture.HttpBaseUrl) };
        MailpitMessagesResponse? result = null;
        for (var attempt = 0; attempt < 20 && (result is null || result.Messages.Count == 0); attempt++)
        {
            await Task.Delay(250);
            result = await httpClient.GetFromJsonAsync<MailpitMessagesResponse>("/api/v1/messages");
        }

        var message = Assert.Single(result!.Messages);
        Assert.Equal("New Snag reported", message.Subject);
    }
}
