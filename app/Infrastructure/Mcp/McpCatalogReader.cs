using System.Text.Json;
using ItmoBot.Application.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ItmoBot.Infrastructure.Mcp;

public sealed class McpCatalogReader(McpCatalogMapper mapper)
{
    public async Task<McpCatalog> ReadAsync(McpClient client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (client.Completion.IsCompleted)
        {
            throw new IOException("Соединение с MCP-сервером закрыто.");
        }

        var tools = client.ServerCapabilities.Tools is null
            ? []
            : await ReadToolsAsync(client, cancellationToken);
        var resources = client.ServerCapabilities.Resources is null
            ? []
            : await ReadResourcesAsync(client, cancellationToken);
        return mapper.Map(tools, resources);
    }

    private async Task<List<Tool>> ReadToolsAsync(McpClient client, CancellationToken cancellationToken)
    {
        var tools = new List<Tool>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var page = await client.ListToolsAsync(new ListToolsRequestParams { Cursor = cursor }, cancellationToken);
            tools.AddRange(page.Tools ?? throw new JsonException("MCP-сервер не вернул список инструментов."));
            cursor = page.NextCursor;
            ValidatePage(cursor, cursors, tools.Count);
        }
        while (cursor is not null);

        return tools;
    }

    private async Task<List<Resource>> ReadResourcesAsync(McpClient client, CancellationToken cancellationToken)
    {
        var resources = new List<Resource>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var page = await client.ListResourcesAsync(new ListResourcesRequestParams { Cursor = cursor }, cancellationToken);
            resources.AddRange(page.Resources ?? throw new JsonException("MCP-сервер не вернул список ресурсов."));
            cursor = page.NextCursor;
            ValidatePage(cursor, cursors, resources.Count);
        }
        while (cursor is not null);

        return resources;
    }

    private void ValidatePage(string? cursor, HashSet<string> cursors, int itemCount)
    {
        if (itemCount > 256 || (cursor is not null
            && (cursor.Length == 0 || !cursors.Add(cursor) || cursors.Count > 32)))
        {
            throw new JsonException("MCP-сервер вернул слишком большой каталог или некорректную пагинацию.");
        }
    }
}
