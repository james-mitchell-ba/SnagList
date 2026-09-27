namespace SnagList.Application.Abstractions;

public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string body, CancellationToken ct);
}
