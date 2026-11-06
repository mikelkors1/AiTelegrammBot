using ModelContextProtocol.Protocol;
using ModelContextProtocol;

namespace ItmoBot.Mcp.Protocol;

public sealed class McpRequestDispatcher(ScheduleMcpEndpoint schedule, WeatherMcpEndpoint weather, ReminderMcpEndpoint reminders)
{
    public ValueTask<ListToolsResult> ListToolsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ListToolsResult { Tools = [ScheduleMcpEndpoint.GetTool(), WeatherMcpEndpoint.GetTool(), ReminderMcpEndpoint.GetTool()] });
    }

    public ValueTask<CallToolResult> CallAsync(CallToolRequestParams? request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return request?.Name switch
        {
            "get_schedule" => schedule.CallAsync(request, cancellationToken),
            "get_weather" => weather.CallAsync(request, cancellationToken),
            "add_reminder" => reminders.CallAsync(request, cancellationToken),
            _ => throw new McpProtocolException("Неизвестный инструмент.", McpErrorCode.InvalidParams),
        };
    }

    public ValueTask<ListResourcesResult> ListResourcesAsync(CancellationToken cancellationToken)
    {
        return schedule.ListResourcesAsync(cancellationToken);
    }

    public ValueTask<ReadResourceResult> ReadAsync(ReadResourceRequestParams? request, CancellationToken cancellationToken)
    {
        return schedule.ReadAsync(request, cancellationToken);
    }
}
