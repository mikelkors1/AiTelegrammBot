using ItmoBot.Shared.Errors;

namespace ItmoBot.Infrastructure.Health.ResultTypes;

public abstract record class HealthEvaluationResult
{
    private HealthEvaluationResult()
    {
    }

    public sealed record class Success(ItmoBot.Infrastructure.Health.HealthReport Report) : HealthEvaluationResult;

    public sealed record class Failure(AppError Error, ItmoBot.Infrastructure.Health.HealthReport Report) : HealthEvaluationResult
    {
        public string Message
        {
            get
            {
                return Error.Message;
            }
        }
    }
}
