using ItmoBot.Application.Mcp;
using ItmoBot.Shared.Results;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FakeMcpSession : IMcpSession
{
    public FakeMcpSession()
    {
        DiscoveryResult = new Result<McpCatalog>.Success(new McpCatalog([], []));
        CheckResult = new Result.Success();
    }

    public Result<McpCatalog> DiscoveryResult
    {
        get; set;
    }

    public Result CheckResult
    {
        get; set;
    }

    public Func<CancellationToken, Task<Result<McpCatalog>>>? OnDiscover
    {
        get; set;
    }

    public int DiscoveryCalls
    {
        get; private set;
    }

    public int CheckCalls
    {
        get; private set;
    }

    public int DisposeCalls
    {
        get; private set;
    }

    public Task<Result<McpCatalog>> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DiscoveryCalls++;
        return OnDiscover is null ? Task.FromResult(DiscoveryResult) : OnDiscover(cancellationToken);
    }

    public Task<Result> CheckConnectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckCalls++;
        return Task.FromResult(CheckResult);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
    }

    public Task<ItmoBot.Application.Agent.ResultTypes.AgentStorageResult<ItmoBot.Application.Agent.Models.ReminderCreation>> ExecuteReminderAsync(
        ItmoBot.Application.Agent.Models.PreparedAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ItmoBot.Application.Agent.ResultTypes.AgentStorageResult<ItmoBot.Application.Agent.Models.ReminderCreation>>(
            new ItmoBot.Application.Agent.ResultTypes.AgentStorageResult<ItmoBot.Application.Agent.Models.ReminderCreation>.Failure(
                new ItmoBot.Shared.Errors.AppError(ItmoBot.Shared.Errors.ErrorCode.McpUnavailable, "Тестовая сессия не выполняет запись.")));
    }

    public Task<Result<McpToolOutput>> CallReadToolAsync(ItmoBot.Application.Llm.LlmToolCall call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Result<McpToolOutput>>(new Result<McpToolOutput>.Failure(
            new ItmoBot.Shared.Errors.AppError(ItmoBot.Shared.Errors.ErrorCode.McpUnavailable, "Тестовый инструмент недоступен.")));
    }

}
