using ItmoBot.Infrastructure.Health.Contracts;
using ItmoBot.Application.Contracts;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.ValueObjects;

namespace ItmoBot.Infrastructure.Health;

public sealed class ReadinessServer : IReadinessServer
{
    private readonly HealthState state;
    private readonly WebApplication server;
    private readonly ILogger? logger;

    public ReadinessServer(IDatabase database, PortNumber port, IHealthServerFactory serverFactory, ILogger? logger = null)
    {
        this.logger = logger;
        state = new HealthState(database);
        server = serverFactory.Create(state, port);
    }

    public CancellationToken Stopping
    {
        get
        {
            return server.Lifetime.ApplicationStopping;
        }
    }

    public async Task<ReadinessServerStartResult> StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await server.StartAsync(cancellationToken);
            return new ReadinessServerStartResult.Success();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            logger?.LogWarning(error, "Не удалось запустить служебный HTTP-сервер.");
            return new ReadinessServerStartResult.Failure(new AppError(
                ErrorCode.HealthEndpointUnavailable,
                "Не удалось запустить служебный HTTP-сервер. Проверьте, что HEALTH_PORT свободен."));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return server.StopAsync(cancellationToken);
    }

    public void MarkPolling(Task pollingTask)
    {
        state.PollingTask = pollingTask;
        state.Initialized = true;
    }

    public void MarkStopped()
    {
        state.Initialized = false;
    }

    public ValueTask DisposeAsync()
    {
        return server.DisposeAsync();
    }
}
