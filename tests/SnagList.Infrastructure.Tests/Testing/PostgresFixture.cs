namespace SnagList.Infrastructure.Tests.Testing;

using Microsoft.EntityFrameworkCore;
using SnagList.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public SnagListDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SnagListDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new SnagListDbContext(options);
    }
}

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
