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
                // Keep Keycloak's native claim names ("roles", "staff_id"). The default
                // inbound mapping would rename "roles" to ClaimTypes.Role, which the
                // Staff/Maintenance policies (reading SnagListClaimTypes.Role) would miss.
                options.MapInboundClaims = false;
                // Local/home-lab Keycloak is fronted over plain HTTP inside the compose network;
                // production (Entra ID, Task in the AWS deployment plan) always uses HTTPS.
                options.RequireHttpsMetadata = false;
                // Keycloak derives the token issuer from the request host, so a token minted
                // via the host-published URL carries a different issuer than metadata fetched
                // over the compose network. Accept both when the extra issuer is configured.
                var additionalIssuer = configuration["Auth:Local:AdditionalValidIssuer"];
                options.TokenValidationParameters.ValidIssuers = string.IsNullOrEmpty(additionalIssuer)
                    ? [authority]
                    : [authority, additionalIssuer];
            });

        return services;
    }
}
