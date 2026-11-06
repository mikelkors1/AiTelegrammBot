using System.Globalization;
using System.Text.Json;
using ItmoBot.Agent.Security;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Application.Llm;
using ItmoBot.Application.Mcp;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OperationResult = ItmoBot.Shared.Results.Result;

namespace ItmoBot.Infrastructure.Mcp;

public sealed class StdioMcpSession(McpClient client, TimeSpan requestTimeout,
    McpErrorMapper errors, TimeProvider clock, ILogger logger, McpCatalogReader catalogReader, ActionContextSigner? contextSigner = null)
    : IMcpSession
{
    private readonly object disposalGate = new();
    private Task? disposal;

    public async Task<Result<McpCatalog>> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(requestTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var catalog = await catalogReader.ReadAsync(client, linked.Token);
            return new Result<McpCatalog>.Success(catalog);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var error = errors.Map(exception);
            logger.LogWarning("Не удалось обнаружить возможности MCP-сервера: {Code}.", error.Code);
            return new Result<McpCatalog>.Failure(error);
        }
    }

    public async Task<OperationResult> CheckConnectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(requestTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            if (string.CompareOrdinal(client.NegotiatedProtocolVersion, "2026-07-28") >= 0)
            {
                await client.SendRequestAsync<DiscoverRequestParams, DiscoverResult>(
                    RequestMethods.ServerDiscover, new DiscoverRequestParams(), cancellationToken: linked.Token);
            }
            else
            {
                await client.PingAsync(cancellationToken: linked.Token);
            }
            return new OperationResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var error = errors.Map(exception);
            logger.LogWarning("Ошибка соединения с MCP-сервером: {Code}.", error.Code);
            return new OperationResult.Failure(error);
        }
    }

    public async Task<AgentStorageResult<ReminderCreation>> ExecuteReminderAsync(PreparedAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (contextSigner is null)
        {
            return new AgentStorageResult<ReminderCreation>.Failure(new AppError(ErrorCode.Unauthorized, "Доверенный контекст соединения недоступен."));
        }

        using var timeout = new CancellationTokenSource(requestTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var result = await client.CallToolAsync(new CallToolRequestParams
            {
                Name = "add_reminder",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["text"] = JsonSerializer.SerializeToElement(action.Payload.Text),
                    ["remind_at"] = JsonSerializer.SerializeToElement(action.Payload.Schedule.Moment.ToString("O", CultureInfo.InvariantCulture)),
                },
                Meta = new ActionContextMetadata().Encode(contextSigner.Sign(action)),
            }, linked.Token);
            return new ReminderResponseMapper().Map(result, action);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var error = errors.Map(exception);
            logger.LogWarning("Ошибка вызова MCP-инструмента: {Code}.", error.Code);
            return new AgentStorageResult<ReminderCreation>.Failure(error);
        }
    }

    public async Task<Result<McpToolOutput>> CallReadToolAsync(LlmToolCall call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (call.Name is not ("get_weather" or "get_schedule"))
        {
            return new Result<McpToolOutput>.Failure(new AppError(ErrorCode.Unauthorized, "Вызов этого инструмента чтения запрещён."));
        }

        using var timeout = new CancellationTokenSource(requestTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var result = await client.CallToolAsync(new CallToolRequestParams
            {
                Name = call.Name,
                Arguments = call.Arguments.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone()),
            }, linked.Token);
            if (result.StructuredContent is not { } value || value.GetRawText().Length > 65536)
            {
                return new Result<McpToolOutput>.Failure(new AppError(ErrorCode.InvalidMcpResponse, "Инструмент вернул некорректный результат."));
            }

            return new Result<McpToolOutput>.Success(new McpToolOutput(value.Clone(), result.IsError == true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new Result<McpToolOutput>.Failure(errors.Map(exception));
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            disposal ??= DisposeCoreAsync();
            return new ValueTask(disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await client.DisposeAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning("Не удалось штатно закрыть MCP-соединение: {Code}.", errors.Map(exception).Code);
        }
    }
}
