namespace SnagList.Api.Auth.Local;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class KeycloakAuthenticationExtensions
{
    public static IServiceCollection AddKeycloakAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var authority = configuration["Auth:Local:Authority"]
            ?? throw new InvalidOperationException("Auth:Local:Authority is required.");
        var audience = configuration["Auth:Local:Audience"]
            ?? throw new InvalidOperationException("Auth:Local:Audience is required.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                // Local/home-lab Keycloak is fronted over plain HTTP inside the compose network;
                // production (Entra ID, Task in the AWS deployment plan) always uses HTTPS.
                options.RequireHttpsMetadata = false;
            });

        return services;
    }
}
