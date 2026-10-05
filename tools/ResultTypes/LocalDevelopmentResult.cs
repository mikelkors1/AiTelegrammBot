using ItmoBot.Shared.Errors;

namespace ItmoBot.Tools.ResultTypes;

public abstract record class LocalDevelopmentResult
{
    private LocalDevelopmentResult()
    {
    }

    public sealed record class Success : LocalDevelopmentResult;

    public sealed record class Failure(AppError Error) : LocalDevelopmentResult;
}
