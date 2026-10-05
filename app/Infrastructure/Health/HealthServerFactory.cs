using ItmoBot.Infrastructure.Health.Contracts;
using ItmoBot.Infrastructure.Health.ResultTypes;
using ItmoBot.Shared.ValueObjects;

namespace ItmoBot.Infrastructure.Health;

public sealed class HealthServerFactory : IHealthServerFactory
{
    public WebApplication Create(HealthState state, PortNumber port)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port.Value}");
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
