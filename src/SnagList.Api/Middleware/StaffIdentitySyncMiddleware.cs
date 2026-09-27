namespace SnagList.Api.Middleware;

using SnagList.Api.Authorization;
using SnagList.Application.Staff.Commands;

public sealed class StaffIdentitySyncMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SyncStaffIdentityCommandHandler handler)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            await handler.HandleAsync(new SyncStaffIdentityCommand(
                context.User.GetStaffId(), context.User.GetStaffName(), context.User.GetStaffEmail(),
                context.User.GetStaffRoles()), context.RequestAborted);
        }
        await next(context);
    }
}
