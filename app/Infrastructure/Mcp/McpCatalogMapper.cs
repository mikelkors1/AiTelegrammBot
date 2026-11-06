using System.Text.Json;
using ItmoBot.Application.Mcp;
using ItmoBot.Shared.Validation;
using ItmoBot.Validation;
using ModelContextProtocol.Protocol;

namespace ItmoBot.Infrastructure.Mcp;

public sealed class McpCatalogMapper
{
    public McpCatalog Map(IEnumerable<Tool> tools, IEnumerable<Resource> resources)
    {
        var mappedTools = tools.Select(tool => tool is null
            ? throw new JsonException("MCP-каталог содержит пустое описание инструмента.")
            : MapTool(tool)).ToArray();
        var mappedResources = resources.Select(resource => resource is null
            ? throw new JsonException("MCP-каталог содержит пустое описание ресурса.")
            : MapResource(resource)).ToArray();
        if (mappedTools.Select(tool => tool.Name).Distinct(StringComparer.Ordinal).Count() != mappedTools.Length
            || mappedResources.Select(resource => resource.Uri).Distinct(StringComparer.Ordinal).Count() != mappedResources.Length)
        {
            throw new JsonException("MCP-каталог содержит повторяющиеся имена инструментов или URI ресурсов.");
        }

        return new McpCatalog(mappedTools, mappedResources);
    }

    private McpToolDefinition MapTool(Tool tool)
    {
        if (tool.OutputSchema is not { } outputSchema)
        {
            throw new JsonException("Инструмент проекта должен предоставлять выходную схему.");
        }

        return new McpToolDefinition(AgentChecks.McpToolName(tool.Name), tool.Description,
            RequestChecks.McpObjectSchema(tool.InputSchema), RequestChecks.McpObjectSchema(outputSchema), tool.Annotations?.ReadOnlyHint == true);
    }

    private McpResourceDefinition MapResource(Resource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Name) || resource.Name.Any(char.IsControl))
        {
            throw new JsonException("MCP-ресурс должен содержать непустое имя без управляющих символов.");
        }

        return new McpResourceDefinition(RequestChecks.McpResourceUri(resource.Uri), resource.Name, resource.Description, resource.MimeType);
    }
}
