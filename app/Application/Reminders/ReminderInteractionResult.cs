using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.Reminders;

public abstract record class ReminderInteractionResult
{
    private ReminderInteractionResult()
    {
    }
    public sealed record class Success(string Reply, bool RemoveButtons) : ReminderInteractionResult;
    public sealed record class Failure(AppError Error, bool RemoveButtons = false) : ReminderInteractionResult;
}
