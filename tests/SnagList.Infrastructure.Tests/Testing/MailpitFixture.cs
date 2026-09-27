namespace SnagList.Infrastructure.Tests.Testing;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

public sealed class MailpitFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder("axllent/mailpit:v1.31.3")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8025).ForPath("/")))
        .Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public string Hostname => _container.Hostname;
    public int SmtpPort => _container.GetMappedPublicPort(1025);
    public string HttpBaseUrl => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8025)}";
}

[CollectionDefinition("Mailpit")]
public sealed class MailpitCollection : ICollectionFixture<MailpitFixture>;
