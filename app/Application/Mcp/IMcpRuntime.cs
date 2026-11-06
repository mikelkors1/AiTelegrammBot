using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Application.Llm;
using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Mcp;

public interface IMcpRuntime : IAsyncDisposable
{
    Task<Result<McpCatalog>> InitializeAsync(CancellationToken cancellationToken);

    Task<Result<McpCatalog>> GetCatalogAsync(CancellationToken cancellationToken);
    Task<Result<McpToolOutput>> CallReadToolAsync(LlmToolCall call, CancellationToken cancellationToken);
    Task<AgentStorageResult<ReminderCreation>> ExecuteReminderAsync(PreparedAction action, CancellationToken cancellationToken);

}
