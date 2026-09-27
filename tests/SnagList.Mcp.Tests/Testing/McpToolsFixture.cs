namespace SnagList.Mcp.Tests.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SnagList.Application;
using SnagList.Application.Abstractions;
using SnagList.Infrastructure;
using SnagList.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class McpToolsFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SnagList"] = _postgres.GetConnectionString(),
            ["Storage:BucketName"] = "test-bucket",
            ["Storage:AccessKey"] = "test",
            ["Storage:SecretKey"] = "test",
            ["Storage:ServiceUrl"] = "http://localhost:9000",
            ["Email:Host"] = "localhost",
            ["Email:FromAddress"] = "snaglist@example.com",
            ["Notifications:MaintenanceTeamEmail"] = "maintenance@example.com",
        }).Build();

        var services = new ServiceCollection();
        services.AddSnagListInfrastructure(configuration);
        services.AddSnagListApplicationHandlers();
        services.RemoveAll(typeof(IBlobStorage));
        services.AddSingleton<IBlobStorage, FakeBlobStorage>();
        services.RemoveAll(typeof(IEmailSender));
        services.AddSingleton<IEmailSender, FakeEmailSender>();

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SnagListDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public IServiceScope CreateScope() => _provider.CreateScope();

    // ActivatorUtilities resolves every constructor parameter from the container except the ones
    // explicitly passed — so this stays correct as SnagTools/LocationTools/MeTools grow more
    // handler dependencies across Tasks 5-7 without needing to change here.
    public T CreateTool<T>(IServiceScope scope, string staffId, params string[] roles) =>
        Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<T>(
            scope.ServiceProvider, FakeHttpContextAccessor.For(staffId, roles));
}

[CollectionDefinition("McpTools")]
public sealed class McpToolsCollection : ICollectionFixture<McpToolsFixture>;
