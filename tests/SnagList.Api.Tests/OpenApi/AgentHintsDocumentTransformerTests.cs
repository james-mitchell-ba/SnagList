namespace SnagList.Api.Tests.OpenApi;

using System.Net.Http.Json;
using System.Text.Json;
using SnagList.Api.Tests.Testing;
using Xunit;

public class AgentHintsDocumentTransformerTests(SnagListApiFactory factory) : IClassFixture<SnagListApiFactory>
{
    [Fact]
    public async Task GetSnag_operation_carries_the_x_mcp_tool_extension()
    {
        var document = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json");

        var getSnagOperation = document.GetProperty("paths").GetProperty("/api/v1/snags/{id}").GetProperty("get");
        Assert.Equal("get_snag", getSnagOperation.GetProperty("x-mcp-tool").GetString());
    }
}
