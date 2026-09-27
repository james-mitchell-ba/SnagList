namespace SnagList.Application;

using Microsoft.Extensions.DependencyInjection;
using SnagList.Application.Locations.Commands;
using SnagList.Application.Locations.Queries;
using SnagList.Application.Snags.Commands;
using SnagList.Application.Snags.Queries;
using SnagList.Application.Staff.Commands;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSnagListApplicationHandlers(this IServiceCollection services) => services
        .AddScoped<CreateLocationCommandHandler>()
        .AddScoped<UpdateLocationCommandHandler>()
        .AddScoped<RetireLocationCommandHandler>()
        .AddScoped<ListLocationsQueryHandler>()
        .AddScoped<ReportSnagCommandHandler>()
        .AddScoped<EditSnagCommandHandler>()
        .AddScoped<WithdrawSnagCommandHandler>()
        .AddScoped<ChangeSnagStatusCommandHandler>()
        .AddScoped<RejectSnagCommandHandler>()
        .AddScoped<AddSnagCommentCommandHandler>()
        .AddScoped<UploadSnagPhotoCommandHandler>()
        .AddScoped<ListSnagsQueryHandler>()
        .AddScoped<GetSnagQueryHandler>()
        .AddScoped<SyncStaffIdentityCommandHandler>();
}
