namespace SnagList.Api.Auth.EntraId;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class EntraIdAuthenticationExtensions
{
    public static IServiceCollection AddEntraIdAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var tenantId = configuration["Auth:EntraId:TenantId"]
            ?? throw new InvalidOperationException("Auth:EntraId:TenantId is required.");
        var audience = configuration["Auth:EntraId:Audience"]
            ?? throw new InvalidOperationException("Auth:EntraId:Audience is required.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
                options.Audience = audience;
                options.RequireHttpsMetadata = true;
            });

        return services;
    }
}
