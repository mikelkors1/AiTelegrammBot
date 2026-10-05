using ItmoBot.Application.Models;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.ResultTypes;

public abstract record class DialogueResult
{
    private DialogueResult()
    {
    }

    public sealed record class Success(DialogueReply Reply) : DialogueResult;
    public sealed record class Failure(AppError Error) : DialogueResult;
}
