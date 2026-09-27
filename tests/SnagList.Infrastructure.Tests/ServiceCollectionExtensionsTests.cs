namespace SnagList.Infrastructure.Tests;

using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SnagList.Application.Abstractions;
using SnagList.Infrastructure;
using SnagList.Infrastructure.Email;
using SnagList.Infrastructure.Storage;
using Xunit;

public class ServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfig(IDictionary<string, string?> overrides)
    {
        var baseValues = new Dictionary<string, string?>
        {
            ["ConnectionStrings:SnagList"] = "Host=localhost;Database=x;Username=x;Password=x",
            ["Storage:BucketName"] = "test-bucket",
        };
        foreach (var (key, value) in overrides) baseValues[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(baseValues).Build();
    }

    [Fact]
    public void Defaults_to_S3Compatible_storage_and_Smtp_email_when_no_provider_is_configured()
    {
        var services = new ServiceCollection();
        services.AddSnagListInfrastructure(BuildConfig(new Dictionary<string, string?>
        {
            ["Storage:AccessKey"] = "x", ["Storage:SecretKey"] = "x", ["Storage:ServiceUrl"] = "http://localhost:9000",
            ["Email:Host"] = "localhost", ["Email:FromAddress"] = "x@example.com",
        }));
        using var provider = services.BuildServiceProvider();

        Assert.IsType<S3CompatibleBlobStorage>(provider.GetRequiredService<IBlobStorage>());
        Assert.IsType<SmtpEmailSender>(provider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void Selects_real_S3_credentials_and_SES_when_configured_for_AWS()
    {
        var services = new ServiceCollection();
        services.AddSnagListInfrastructure(BuildConfig(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "S3",
            ["Email:Provider"] = "Ses",
            ["Email:FromAddress"] = "snaglist@example.com",
        }));
        using var provider = services.BuildServiceProvider();

        Assert.IsType<S3CompatibleBlobStorage>(provider.GetRequiredService<IBlobStorage>());
        Assert.IsType<SesEmailSender>(provider.GetRequiredService<IEmailSender>());
    }
}
