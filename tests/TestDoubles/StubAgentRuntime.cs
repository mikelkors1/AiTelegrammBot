using ItmoBot.Application.Llm;
using ItmoBot.Application.Mcp;
using ItmoBot.Shared.Results;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class StubAgentRuntime(Result<McpCatalog> catalog) : IMcpRuntime
{
    public readonly List<LlmToolCall> Calls = [];
    public Func<LlmToolCall, Result<McpToolOutput>>? OnCall
    {
        get; set;
    }

    public Task<Result<McpCatalog>> InitializeAsync(CancellationToken cancellationToken)
    {
        return GetCatalogAsync(cancellationToken);
    }

    public Task<Result<McpCatalog>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(catalog);
    }

    public Task<Result<McpToolOutput>> CallReadToolAsync(LlmToolCall call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(call);
        return Task.FromResult(OnCall!(call));
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
    public Task<ItmoBot.Application.Agent.ResultTypes.AgentStorageResult<ItmoBot.Application.Agent.Models.ReminderCreation>> ExecuteReminderAsync(
        ItmoBot.Application.Agent.Models.PreparedAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ItmoBot.Application.Agent.ResultTypes.AgentStorageResult<ItmoBot.Application.Agent.Models.ReminderCreation>>(
            new ItmoBot.Application.Agent.ResultTypes.AgentStorageResult<ItmoBot.Application.Agent.Models.ReminderCreation>.Failure(
                new ItmoBot.Shared.Errors.AppError(ItmoBot.Shared.Errors.ErrorCode.McpUnavailable, "Тестовый MCP недоступен.")));
    }

}
