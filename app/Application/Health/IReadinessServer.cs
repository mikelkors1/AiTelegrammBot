using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Health;

public interface IReadinessServer : IAsyncDisposable
{
    CancellationToken Stopping
    {
        get;
    }
    Task<Result> StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    void MarkPolling(Task pollingTask);
    void MarkStopped();
}
