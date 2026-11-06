using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Application.Llm;
using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Mcp;

public interface IMcpSession : IAsyncDisposable
{
    Task<Result> CheckConnectionAsync(CancellationToken cancellationToken);

    Task<Result<McpCatalog>> DiscoverAsync(CancellationToken cancellationToken);

    Task<AgentStorageResult<ReminderCreation>> ExecuteReminderAsync(PreparedAction action, CancellationToken cancellationToken);
    Task<Result<McpToolOutput>> CallReadToolAsync(LlmToolCall call, CancellationToken cancellationToken);

}
