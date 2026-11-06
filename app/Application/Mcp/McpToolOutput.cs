using System.Text.Json;

namespace ItmoBot.Application.Mcp;

public sealed record class McpToolOutput(JsonElement Data, bool IsError);
