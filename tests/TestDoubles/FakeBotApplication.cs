using ItmoBot.Application;
using ItmoBot.Shared.Results;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FakeBotApplication(Func<CancellationToken, Task<Result>> run) : IBotApplication
{
    public Task<Result> RunAsync(CancellationToken cancellationToken)
    {
        return run(cancellationToken);
    }
}
