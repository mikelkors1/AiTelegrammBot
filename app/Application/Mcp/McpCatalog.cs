namespace ItmoBot.Application.Mcp;

public sealed class McpCatalog
{
    public McpCatalog(IEnumerable<McpToolDefinition> tools, IEnumerable<McpResourceDefinition> resources)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(resources);
        Tools = Array.AsReadOnly(tools.ToArray());
        Resources = Array.AsReadOnly(resources.ToArray());
    }

    public IReadOnlyList<McpToolDefinition> Tools
    {
        get;
    }

    public IReadOnlyList<McpResourceDefinition> Resources
    {
        get;
    }
}
