namespace SnagList.Mcp.Tests.Testing;

using SnagList.Application.Abstractions;

public sealed class FakeEmailSender : IEmailSender
{
    public Task SendAsync(string toAddress, string subject, string body, CancellationToken ct) => Task.CompletedTask;
}
