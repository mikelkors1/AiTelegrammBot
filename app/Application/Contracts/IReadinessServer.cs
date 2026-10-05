using ItmoBot.Application.ResultTypes;

namespace ItmoBot.Application.Contracts;

public interface IReadinessServer : IAsyncDisposable
{
    CancellationToken Stopping
    {
        get;
    }
    Task<ReadinessServerStartResult> StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    void MarkPolling(Task pollingTask);
    void MarkStopped();
}
