namespace SnagList.Api.Tests.Testing;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SnagList.Application.Abstractions;
using SnagList.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class SnagListApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SnagListDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Storage:BucketName", "test-bucket");
        builder.UseSetting("Storage:AccessKey", "test");
        builder.UseSetting("Storage:SecretKey", "test");
        builder.UseSetting("Storage:ServiceUrl", "http://localhost:9000");
        builder.UseSetting("Email:Host", "localhost");
        builder.UseSetting("Email:FromAddress", "snaglist@example.com");
        builder.UseSetting("Auth:Local:Authority", "http://localhost:8080/realms/snaglist");
        builder.UseSetting("Auth:Local:Audience", "snaglist-api");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<SnagListDbContext>>();
            services.AddScoped<DbContextOptions<SnagListDbContext>>(_ =>
                new DbContextOptionsBuilder<SnagListDbContext>()
                    .UseNpgsql(_postgres.GetConnectionString()).Options);

            services.RemoveAll<IBlobStorage>();
            services.AddSingleton<IBlobStorage, FakeBlobStorage>();
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, FakeEmailSender>();

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    public HttpClient CreateAuthenticatedClient(string staffId, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-StaffId", staffId);
        client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(',', roles));
        return client;
    }

    public HttpClient CreateAuthenticatedClientNoRedirect(string staffId, params string[] roles)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-StaffId", staffId);
        client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(',', roles));
        return client;
    }
}
