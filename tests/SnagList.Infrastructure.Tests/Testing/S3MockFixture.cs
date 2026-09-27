namespace SnagList.Infrastructure.Tests.Testing;

using Amazon.Runtime;
using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

public sealed class S3MockFixture : IAsyncLifetime
{
    public const string BucketName = "snaglist-photos-test";

    private readonly IContainer _container = new ContainerBuilder("adobe/s3mock:5.2.3")
        .WithPortBinding(9090, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Started S3MockApplication"))
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await CreateClient().PutBucketAsync(BucketName);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public IAmazonS3 CreateClient() => new AmazonS3Client(
        new BasicAWSCredentials("test", "test"),
        new AmazonS3Config
        {
            ServiceURL = $"http://localhost:{_container.GetMappedPublicPort(9090)}",
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
        });
}

[CollectionDefinition("S3Mock")]
public sealed class S3MockCollection : ICollectionFixture<S3MockFixture>;
