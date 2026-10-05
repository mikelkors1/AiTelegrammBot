using ItmoBot.Application.Models;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.ResultTypes;

public abstract record class PromptResolutionResult
{
    private PromptResolutionResult()
    {
    }

    public sealed record class Success(PromptDefinition Definition) : PromptResolutionResult;

    public sealed record class Failure(AppError Error) : PromptResolutionResult;
}
