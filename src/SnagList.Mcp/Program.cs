using Amazon.Lambda.AspNetCoreServer.Hosting;
using SnagList.Api.Auth.EntraId;
using SnagList.Api.Auth.Local;
using SnagList.Application;
using SnagList.Authorization;
using SnagList.Infrastructure;
using SnagList.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
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

app.Run();

public partial class Program;
