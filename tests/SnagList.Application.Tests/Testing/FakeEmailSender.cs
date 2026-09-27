namespace SnagList.Application.Tests.Testing;

using SnagList.Application.Abstractions;

public sealed class FakeEmailSender : IEmailSender
{
    public readonly List<(string To, string Subject)> SentEmails = [];

    public Task SendAsync(string toAddress, string subject, string body, CancellationToken ct)
    {
        SentEmails.Add((toAddress, subject));
        return Task.CompletedTask;
    }
}
