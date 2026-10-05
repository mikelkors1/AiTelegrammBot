using ItmoBot.Application.ResultTypes;

namespace ItmoBot.Application.Contracts;

public interface IBotApplication
{
    Task<BotExecutionResult> RunAsync(CancellationToken cancellationToken);
}
