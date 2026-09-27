namespace SnagList.Mcp.Tests;

using System.Reflection;
using ModelContextProtocol.Server;
using SnagList.Application;
using Xunit;

public class AgentOperationCatalogParityTests
{
    private static HashSet<string> DiscoverRegisteredToolNames()
    {
        var toolTypes = typeof(SnagList.Mcp.Tools.SnagTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null);

        var names = new HashSet<string>();
        foreach (var type in toolTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>()?.Name is { } name) names.Add(name);
            }
        }
        return names;
    }

    [Fact]
    public void Every_registered_MCP_tool_has_a_matching_AgentOperationCatalog_entry()
    {
        var registered = DiscoverRegisteredToolNames();
        var cataloged = AgentOperationCatalog.OperationIdToMcpTool.Values.ToHashSet();

        var orphanedTools = registered.Except(cataloged).ToList();
        Assert.True(orphanedTools.Count == 0,
            $"Registered MCP tool(s) with no AgentOperationCatalog entry: {string.Join(", ", orphanedTools)}");
    }

    [Fact]
    public void Every_AgentOperationCatalog_entry_has_a_matching_registered_MCP_tool()
    {
        var registered = DiscoverRegisteredToolNames();
        var cataloged = AgentOperationCatalog.OperationIdToMcpTool.Values;

        var missingTools = cataloged.Except(registered).ToList();
        Assert.True(missingTools.Count == 0,
            $"AgentOperationCatalog entry/entries with no registered MCP tool: {string.Join(", ", missingTools)}");
    }
}
