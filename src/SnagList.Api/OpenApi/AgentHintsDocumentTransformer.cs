namespace SnagList.Api.OpenApi;

using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SnagList.Application;

public sealed class AgentHintsDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        if (document.Paths is null) return Task.CompletedTask;
        foreach (var path in document.Paths.Values)
        {
            if (path?.Operations is null) continue;
            foreach (var operation in path.Operations.Values)
            {
                if (operation.OperationId is null) continue;
                if (!AgentOperationCatalog.OperationIdToMcpTool.TryGetValue(operation.OperationId, out var toolName)) continue;
                operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();

                operation.Extensions["x-mcp-tool"] = new JsonNodeExtension(JsonValue.Create(toolName)!);
                operation.Extensions["x-agent-hints"] = new JsonNodeExtension(JsonValue.Create(
                    $"Corresponds 1:1 to the '{toolName}' MCP tool; the same authorization applies to both.")!);
            }
        }
        return Task.CompletedTask;
    }
}
