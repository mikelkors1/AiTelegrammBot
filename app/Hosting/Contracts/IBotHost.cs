using ItmoBot.Application.ResultTypes;

namespace ItmoBot.Hosting.Contracts;

public interface IBotHost
{
    Task<BotExecutionResult> RunAsync(CancellationToken cancellationToken);
}
