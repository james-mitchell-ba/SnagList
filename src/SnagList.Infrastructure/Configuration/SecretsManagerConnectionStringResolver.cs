namespace SnagList.Infrastructure.Configuration;

using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

public sealed class SecretsManagerConnectionStringResolver(IAmazonSecretsManager secretsManager)
{
    private sealed record DbCredentials(string Username, string Password);

    public async Task<string> ResolveConnectionStringAsync(
        string secretArn, string host, int port, string databaseName, CancellationToken ct)
    {
        var response = await secretsManager.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretArn }, ct);
        var credentials = JsonSerializer.Deserialize<DbCredentials>(response.SecretString)
            ?? throw new InvalidOperationException($"Secret '{secretArn}' did not contain the expected username/password shape.");
        return $"Host={host};Port={port};Database={databaseName};Username={credentials.Username};Password={credentials.Password}";
    }
}
