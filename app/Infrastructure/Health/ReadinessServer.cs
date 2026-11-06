using ItmoBot.Application.Health;
using ItmoBot.Application.Storage;
using ItmoBot.Infrastructure.Health.ResultTypes;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;

namespace ItmoBot.Infrastructure.Health;

public sealed class ReadinessServer : IReadinessServer
{
    private readonly HealthState state;
    private readonly WebApplication server;
    private readonly ILogger? logger;

    public ReadinessServer(IDatabase database, int port, ILogger? logger = null)
    {
        this.logger = logger;
        state = new HealthState(database);
        server = CreateServer(state, port);
    }

    public CancellationToken Stopping
    {
        get
        {
            return server.Lifetime.ApplicationStopping;
        }
    }

    public async Task<Result> StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await server.StartAsync(cancellationToken);
            return new Result.Success();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            logger?.LogWarning(error, "Не удалось запустить служебный HTTP-сервер.");
            return new Result.Failure(new AppError(
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

    public static WebApplication CreateServer(HealthState state, int port)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var server = builder.Build();
        server.MapGet("/health", async (CancellationToken cancellationToken) =>
        {
            var result = await state.CheckAsync(cancellationToken);
            var report = result switch
            {
                HealthEvaluationResult.Success success => success.Report,
                HealthEvaluationResult.Failure failure => failure.Report,
                _ => throw new InvalidOperationException("Неизвестный результат readiness."),
            };
            var body = new Dictionary<string, string>
            {
                ["status"] = report.IsReady ? "ok" : "not_ready",
                ["database"] = report.DatabaseAvailable ? "ok" : "unavailable",
                ["polling"] = report.PollingRunning ? "running" : "stopped",
            };
            return Results.Json(body, statusCode: report.IsReady ? 200 : 503);
        });
        return server;
    }
}
