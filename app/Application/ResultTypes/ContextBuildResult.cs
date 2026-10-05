using ItmoBot.Application.Models;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.ResultTypes;

public abstract record class ContextBuildResult
{
    private ContextBuildResult()
    {
    }

    public sealed record class Success(PreparedLlmRequest Prepared) : ContextBuildResult;

    public sealed record class Failure(AppError Error) : ContextBuildResult;
}
