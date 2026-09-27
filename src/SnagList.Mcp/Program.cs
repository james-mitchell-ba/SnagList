using Amazon.Lambda.AspNetCoreServer.Hosting;
using Amazon.SecretsManager;
using SnagList.Api.Auth.EntraId;
using SnagList.Api.Auth.Local;
using SnagList.Infrastructure.Configuration;
using SnagList.Application;
using SnagList.Authorization;
using SnagList.Infrastructure;
using SnagList.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
if (builder.Configuration["Database:SecretArn"] is { } dbSecretArn)
{
    var resolver = new SecretsManagerConnectionStringResolver(new AmazonSecretsManagerClient());
    var connectionString = await resolver.ResolveConnectionStringAsync(
        dbSecretArn,
        builder.Configuration["Database:Host"] ?? throw new InvalidOperationException("Database:Host is required."),
        int.Parse(builder.Configuration["Database:Port"] ?? "5432"),
        builder.Configuration["Database:Name"] ?? throw new InvalidOperationException("Database:Name is required."),
        default);
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SnagList"] = connectionString });
}
builder.Services.AddSnagListInfrastructure(builder.Configuration);
builder.Services.AddSnagListApplicationHandlers();
builder.Services.AddHttpContextAccessor();

switch (builder.Configuration["Auth:Provider"] ?? "Local")
{
    case "Local":
        builder.Services.AddKeycloakAuthentication(builder.Configuration);
        break;
    case "EntraId":
        builder.Services.AddEntraIdAuthentication(builder.Configuration);
        break;
    default:
        throw new InvalidOperationException($"Unknown Auth:Provider '{builder.Configuration["Auth:Provider"]}'.");
}
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PolicyNames.Staff, policy => policy.RequireAssertion(ctx => AuthorizationPolicies.IsStaff(ctx.User.GetStaffRoles())))
    .AddPolicy(PolicyNames.Maintenance, policy => policy.RequireAssertion(ctx => AuthorizationPolicies.IsMaintenance(ctx.User.GetStaffRoles())));

builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<SnagTools>()
    .WithTools<LocationTools>()
    .WithTools<MeTools>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapMcp("/mcp").RequireAuthorization(PolicyNames.Staff);

await app.RunAsync();

public partial class Program;
