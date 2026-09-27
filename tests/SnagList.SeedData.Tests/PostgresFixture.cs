namespace SnagList.SeedData.Tests;

using Microsoft.EntityFrameworkCore;
using SnagList.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public SnagListDbContext CreateContext() => new(
        new DbContextOptionsBuilder<SnagListDbContext>().UseNpgsql(_container.GetConnectionString()).Options);
}
