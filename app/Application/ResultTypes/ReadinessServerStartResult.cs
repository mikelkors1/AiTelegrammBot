using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.ResultTypes;

public abstract record class ReadinessServerStartResult
{
    private ReadinessServerStartResult()
    {
    }

    public sealed record class Success : ReadinessServerStartResult;

    public sealed record class Failure(AppError Error) : ReadinessServerStartResult;
}
