namespace SnagList.Infrastructure;

using Amazon.Runtime;
using Amazon.S3;
using Amazon.SimpleEmailV2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SnagList.Application.Abstractions;
using SnagList.Application.Notifications;
using SnagList.Infrastructure.Audit;
using SnagList.Infrastructure.Clock;
using SnagList.Infrastructure.Email;
using SnagList.Infrastructure.Persistence;
using SnagList.Infrastructure.Persistence.Queries;
using SnagList.Infrastructure.Persistence.Repositories;
using SnagList.Infrastructure.Storage;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSnagListInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SnagListDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("SnagList")
                ?? throw new InvalidOperationException("Connection string 'SnagList' is required.")));

        services.AddScoped<ILocationRepository, EfLocationRepository>();
        services.AddScoped<ISnagRepository, EfSnagRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ILocationQueries, EfLocationQueries>();
        services.AddScoped<ISnagQueries, EfSnagQueries>();
        services.AddScoped<IAuditWriter, EfAuditWriter>();
        services.AddScoped<IStaffIdentityRepository, EfStaffIdentityRepository>();
        services.AddSingleton<IClock, SystemClock>();

        var storage = configuration.GetSection("Storage");
        var storageBucket = storage["BucketName"] ?? throw new InvalidOperationException("Storage:BucketName is required.");
        switch (storage["Provider"] ?? "S3Compatible")
        {
            case "S3Compatible":
                services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
                    new BasicAWSCredentials(
                        storage["AccessKey"] ?? throw new InvalidOperationException("Storage:AccessKey is required."),
                        storage["SecretKey"] ?? throw new InvalidOperationException("Storage:SecretKey is required.")),
                    new AmazonS3Config
                    {
                        ServiceURL = storage["ServiceUrl"] ?? throw new InvalidOperationException("Storage:ServiceUrl is required."),
                        ForcePathStyle = true,
                    }));
                break;
            case "S3":
                // Real S3: credentials come from the Lambda execution role via the default AWS credential
                // chain, never from configuration — there is nothing else to set here.
                services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
                break;
            default:
                throw new InvalidOperationException($"Unknown Storage:Provider '{storage["Provider"]}'.");
        }
        services.AddSingleton<IBlobStorage>(sp => new S3CompatibleBlobStorage(sp.GetRequiredService<IAmazonS3>(), storageBucket));

        switch (configuration["Email:Provider"] ?? "Smtp")
        {
            case "Smtp":
                services.Configure<SmtpEmailSenderOptions>(configuration.GetSection("Email"));
                services.AddSingleton<IEmailSender>(sp => new SmtpEmailSender(sp.GetRequiredService<IOptions<SmtpEmailSenderOptions>>().Value));
                break;
            case "Ses":
                services.AddSingleton<IAmazonSimpleEmailServiceV2>(_ => new AmazonSimpleEmailServiceV2Client());
                services.Configure<SesEmailSenderOptions>(configuration.GetSection("Email"));
                services.AddSingleton<IEmailSender>(sp => new SesEmailSender(
                    sp.GetRequiredService<IAmazonSimpleEmailServiceV2>(), sp.GetRequiredService<IOptions<SesEmailSenderOptions>>().Value));
                break;
            default:
                throw new InvalidOperationException($"Unknown Email:Provider '{configuration["Email:Provider"]}'.");
        }

        services.Configure<NotificationOptions>(configuration.GetSection("Notifications"));
        services.AddScoped(sp => sp.GetRequiredService<IOptions<NotificationOptions>>().Value);
        services.AddScoped<SnagNotificationDispatcher>();

        return services;
    }
}
