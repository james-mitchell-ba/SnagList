using Amazon.Runtime;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

builder.Services.AddSnagListInfrastructure(builder.Configuration);

builder.Services.AddSnagListApplicationHandlers();

builder.Services.AddKeycloakAuthentication(builder.Configuration);
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

app.Run();

public partial class Program; // exposes the entry point for WebApplicationFactory<Program> in tests
