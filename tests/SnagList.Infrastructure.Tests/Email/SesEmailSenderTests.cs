namespace SnagList.Infrastructure.Tests.Email;

using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using NSubstitute;
using SnagList.Infrastructure.Email;
using Xunit;

public class SesEmailSenderTests
{
    [Fact]
    public async Task SendAsync_calls_SES_with_the_expected_fields()
    {
        var sesClient = Substitute.For<IAmazonSimpleEmailServiceV2>();
        var sender = new SesEmailSender(sesClient, new SesEmailSenderOptions { FromAddress = "snaglist@example.com" });

        await sender.SendAsync("maintenance@example.com", "New Snag reported", "A Snag was reported.", default);

        await sesClient.Received(1).SendEmailAsync(
            Arg.Is<SendEmailRequest>(r =>
                r.FromEmailAddress == "snaglist@example.com" &&
                r.Destination.ToAddresses.Contains("maintenance@example.com") &&
                r.Content.Simple.Subject.Data == "New Snag reported" &&
                r.Content.Simple.Body.Text.Data == "A Snag was reported."),
            Arg.Any<CancellationToken>());
    }
}
