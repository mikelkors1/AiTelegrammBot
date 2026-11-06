using System.Text.Json;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;
using ModelContextProtocol;

namespace ItmoBot.Infrastructure.Mcp;

public sealed class McpErrorMapper
{
    public AppError Map(Exception error)
    {
        return error switch
        {
            InputValidationException { Code: ErrorCode.InvalidMcpResponse } => new AppError(
                ErrorCode.InvalidMcpResponse, "MCP-сервер вернул некорректные описания инструментов или ресурсов."),
            OperationCanceledException or TimeoutException => new AppError(
                ErrorCode.McpTimeout, "Превышено время ожидания MCP-сервера.", IsRetryable: true),
            McpProtocolException or JsonException => new AppError(
                ErrorCode.InvalidMcpResponse, "MCP-сервер вернул некорректный ответ протокола."),
            _ => new AppError(ErrorCode.McpUnavailable,
                "Не удалось связаться с MCP-сервером. Инструменты временно недоступны.", IsRetryable: true),
        };
    }
}
