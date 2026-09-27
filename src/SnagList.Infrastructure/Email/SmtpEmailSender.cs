namespace SnagList.Infrastructure.Email;

using MailKit.Net.Smtp;
using MimeKit;
using SnagList.Application.Abstractions;

public sealed class SmtpEmailSender(SmtpEmailSenderOptions options) : IEmailSender
{
    public async Task SendAsync(string toAddress, string subject, string body, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(options.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(options.Host, options.Port, options.UseTls, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
