using SnagList.Api.Auth.Local;
using SnagList.Application;
using SnagList.Authorization;
using SnagList.Infrastructure;
using SnagList.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSnagListInfrastructure(builder.Configuration);
builder.Services.AddSnagListApplicationHandlers();

builder.Services.AddKeycloakAuthentication(builder.Configuration);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PolicyNames.Staff, policy => policy.RequireAssertion(ctx => AuthorizationPolicies.IsStaff(ctx.User.GetStaffRoles())))
    .AddPolicy(PolicyNames.Maintenance, policy => policy.RequireAssertion(ctx => AuthorizationPolicies.IsMaintenance(ctx.User.GetStaffRoles())));

builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<SnagTools>();
    // .WithTools<LocationTools>().WithTools<MeTools>() added in Tasks 6-7,
    // once those classes exist — chaining a tool type that doesn't exist yet won't compile.

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapMcp("/mcp").RequireAuthorization(PolicyNames.Staff);

app.Run();

public partial class Program;
