using ItmoBot.Application.Storage;
using ItmoBot.Infrastructure.Health.ResultTypes;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;

namespace ItmoBot.Infrastructure.Health;

public sealed class HealthState(IDatabase database)
{
    public bool Initialized
    {
        get; set;
    }
    public Task? PollingTask
    {
        get; set;
    }

    public async Task<HealthEvaluationResult> CheckAsync(CancellationToken cancellationToken)
    {
        var check = await database.CheckAsync(cancellationToken);
        var report = new HealthReport(check is Result.Success,
            PollingTask is { IsCompleted: false }, Initialized);
        if (check is Result.Failure databaseFailure)
        {
            return new HealthEvaluationResult.Failure(databaseFailure.Error, report);
        }

        if (!report.IsReady)
        {
            return new HealthEvaluationResult.Failure(new AppError(
                ErrorCode.ApplicationNotReady,
                "Приложение не готово: инициализация не завершена или polling остановлен."), report);
        }

        return new HealthEvaluationResult.Success(report);
    }
}
