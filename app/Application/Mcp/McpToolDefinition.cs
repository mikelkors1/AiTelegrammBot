using System.Text.Json;

namespace ItmoBot.Application.Mcp;

public sealed record class McpToolDefinition(string Name, string? Description,
    JsonElement InputSchema, JsonElement OutputSchema, bool ReadOnlyHint);
