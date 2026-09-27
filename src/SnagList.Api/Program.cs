using Amazon.Lambda.AspNetCoreServer.Hosting;
using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SnagList.Api.Auth.EntraId;
using SnagList.Api.Auth.Local;
using SnagList.Authorization;
using SnagList.Api.ErrorHandling;
using SnagList.Application;
using SnagList.Application.Abstractions;
using SnagList.Application.Locations.Commands;
using SnagList.Application.Locations.Queries;
using SnagList.Application.Notifications;
using SnagList.Application.Snags.Commands;
using SnagList.Application.Snags.Queries;
using SnagList.Infrastructure.Audit;
using SnagList.Infrastructure.Clock;
using SnagList.Infrastructure.Configuration;
using SnagList.Infrastructure.Email;
using SnagList.Api.Endpoints;
using SnagList.Api.Middleware;
using SnagList.Api.OpenApi;
using SnagList.Infrastructure;
using SnagList.Application.Staff.Commands;
using SnagList.Infrastructure.Persistence;
using SnagList.Infrastructure.Persistence.Queries;
using SnagList.Infrastructure.Persistence.Repositories;
using SnagList.Infrastructure.Storage;

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

builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<AgentHintsDocumentTransformer>());

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<StaffIdentitySyncMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapOpenApi("/openapi/v1.json");

app.MapLocationEndpoints();
app.MapSnagEndpoints();
app.MapMeEndpoints();

await app.RunAsync();

public partial class Program; // exposes the entry point for WebApplicationFactory<Program> in tests
