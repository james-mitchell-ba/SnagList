namespace SnagList.Infrastructure.Tests.Testing;

using SnagList.Application.Abstractions;

public sealed class NoOpEmailSender : IEmailSender
{
    public Task SendAsync(string toAddress, string subject, string body, CancellationToken ct) => Task.CompletedTask;
}
