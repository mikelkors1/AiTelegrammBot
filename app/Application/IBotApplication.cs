using ItmoBot.Shared.Results;

namespace ItmoBot.Application;

public interface IBotApplication
{
    Task<Result> RunAsync(CancellationToken cancellationToken);
}
