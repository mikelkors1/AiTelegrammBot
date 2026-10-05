using ItmoBot.Shared.Errors;

namespace ItmoBot.Tools.ResultTypes;

public abstract record class EnvironmentPreparationResult
{
    private EnvironmentPreparationResult()
    {
    }

    public sealed record class Success : EnvironmentPreparationResult;

    public sealed record class Failure(AppError Error) : EnvironmentPreparationResult;
}
