namespace SnagList.Infrastructure.Email;

using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using SnagList.Application.Abstractions;

public sealed class SesEmailSender(IAmazonSimpleEmailServiceV2 sesClient, SesEmailSenderOptions options) : IEmailSender
{
    public Task SendAsync(string toAddress, string subject, string body, CancellationToken ct) =>
        sesClient.SendEmailAsync(new SendEmailRequest
        {
            FromEmailAddress = options.FromAddress,
            Destination = new Destination { ToAddresses = [toAddress] },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = subject },
                    Body = new Body { Text = new Content { Data = body } },
                },
            },
        }, ct);
}
