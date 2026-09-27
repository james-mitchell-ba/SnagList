using Amazon.Runtime;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SnagList.Api.Auth.Local;
using SnagList.Api.Authorization;
using SnagList.Api.ErrorHandling;
using SnagList.Application.Abstractions;
using SnagList.Infrastructure.Audit;
using SnagList.Infrastructure.Clock;
using SnagList.Infrastructure.Email;
using SnagList.Infrastructure.Persistence;
using SnagList.Infrastructure.Persistence.Queries;
using SnagList.Infrastructure.Persistence.Repositories;
using SnagList.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SnagListDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("SnagList")
        ?? throw new InvalidOperationException("Connection string 'SnagList' is required.")));

builder.Services.AddScoped<ILocationRepository, EfLocationRepository>();
builder.Services.AddScoped<ISnagRepository, EfSnagRepository>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<ILocationQueries, EfLocationQueries>();
builder.Services.AddScoped<ISnagQueries, EfSnagQueries>();
builder.Services.AddScoped<IAuditWriter, EfAuditWriter>();
builder.Services.AddSingleton<IClock, SystemClock>();

var storage = builder.Configuration.GetSection("Storage");
var storageBucket = storage["BucketName"] ?? throw new InvalidOperationException("Storage:BucketName is required.");
builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
    new BasicAWSCredentials(
        storage["AccessKey"] ?? throw new InvalidOperationException("Storage:AccessKey is required."),
        storage["SecretKey"] ?? throw new InvalidOperationException("Storage:SecretKey is required.")),
    new AmazonS3Config
    {
        ServiceURL = storage["ServiceUrl"] ?? throw new InvalidOperationException("Storage:ServiceUrl is required."),
        ForcePathStyle = true,
    }));
builder.Services.AddSingleton<IBlobStorage>(sp => new S3CompatibleBlobStorage(sp.GetRequiredService<IAmazonS3>(), storageBucket));

builder.Services.Configure<SmtpEmailSenderOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddSingleton<IEmailSender>(sp =>
    new SmtpEmailSender(sp.GetRequiredService<IOptions<SmtpEmailSenderOptions>>().Value));

builder.Services.AddKeycloakAuthentication(builder.Configuration);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PolicyNames.Staff, policy => policy.RequireAssertion(ctx => AuthorizationPolicies.IsStaff(ctx.User.GetStaffRoles())))
    .AddPolicy(PolicyNames.Maintenance, policy => policy.RequireAssertion(ctx => AuthorizationPolicies.IsMaintenance(ctx.User.GetStaffRoles())));

builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapOpenApi("/openapi/v1.json");

app.Run();

public partial class Program; // exposes the entry point for WebApplicationFactory<Program> in tests
