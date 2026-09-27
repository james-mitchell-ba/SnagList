namespace SnagList.SeedData.Tests.Keycloak;

using Testcontainers.Keycloak;
using Xunit;

public sealed class KeycloakFixture : IAsyncLifetime
{
    // Set explicitly via WithEnvironment (a base Testcontainers builder method every module
    // supports) rather than relying on this module's own default-credential accessors, whose
    // exact names aren't certain enough to depend on here.
    public const string AdminUsername = "admin";
    public const string AdminPassword = "admin";

    private readonly KeycloakContainer _container = new KeycloakBuilder("quay.io/keycloak/keycloak:26.0")
        .WithEnvironment("KEYCLOAK_ADMIN", AdminUsername)
        .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", AdminPassword)
        .Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public string BaseAddress => _container.GetBaseAddress().TrimEnd('/');
}

[CollectionDefinition("Keycloak")]
public sealed class KeycloakCollection : ICollectionFixture<KeycloakFixture>;
