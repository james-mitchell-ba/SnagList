namespace SnagList.Infrastructure.Tests.Configuration;

using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using NSubstitute;
using SnagList.Infrastructure.Configuration;
using Xunit;

public class SecretsManagerConnectionStringResolverTests
{
    [Fact]
    public async Task ResolveConnectionStringAsync_builds_the_connection_string_from_the_secret()
    {
        var secretsManager = Substitute.For<IAmazonSecretsManager>();
        secretsManager.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetSecretValueResponse { SecretString = """{"Username":"snaglist_app","Password":"s3cr3t"}""" });
        var resolver = new SecretsManagerConnectionStringResolver(secretsManager);

        var connectionString = await resolver.ResolveConnectionStringAsync(
            "arn:aws:secretsmanager:...", "db.example.com", 5432, "snaglist", default);

        Assert.Contains("Host=db.example.com", connectionString);
        Assert.Contains("Port=5432", connectionString);
        Assert.Contains("Database=snaglist", connectionString);
        Assert.Contains("Username=snaglist_app", connectionString);
        Assert.Contains("Password=s3cr3t", connectionString);
    }
}
