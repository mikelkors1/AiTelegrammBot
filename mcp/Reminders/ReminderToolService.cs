using ItmoBot.Agent.Security;
using ItmoBot.Application.Agent.Contracts;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Application.Agent;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Mcp.Reminders;

public sealed class ReminderToolService(
    ActionContextVerifier verifier,
    IPreparedActionRepository actions,
    IReminderRepository reminders,
    ReminderPayloadHasher hasher)

{
    public async Task<AgentStorageResult<ReminderCreation>> CreateReminderAsync(string text, DateTimeOffset moment, TrustedActionContext? context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!verifier.Verify(context))
        {
            return Unauthorized();
        }

        var trusted = context!;
        var stored = await actions.GetActionAsync(trusted.Owner, trusted.ActionId, cancellationToken);
        if (stored is AgentStorageResult<PreparedAction>.Failure failure)
        {
            return new AgentStorageResult<ReminderCreation>.Failure(failure.Error);
        }

        if (stored is not AgentStorageResult<PreparedAction>.Success success)
        {
            return Unauthorized();
        }

        ReminderPayload payload;
        try
        {
            payload = new ReminderPayload(text, new ReminderSchedule(moment, success.Value.Payload.Schedule.TimeZone));
        }
        catch (ArgumentException)
        {
            return new AgentStorageResult<ReminderCreation>.Failure(new AppError(ErrorCode.ActionMismatch, "Время не соответствует подтверждённым параметрам."));
        }

        if (hasher.Hash(payload) != trusted.ArgumentsHash)
        {
            return new AgentStorageResult<ReminderCreation>.Failure(new AppError(ErrorCode.ActionMismatch, "Параметры не совпадают с подтверждённым действием."));
        }

        return await reminders.ExecuteConfirmedActionAsync(trusted.Owner, trusted.ActionId, trusted.ArgumentsHash, cancellationToken);
    }

    private AgentStorageResult<ReminderCreation> Unauthorized()
    {
        return new AgentStorageResult<ReminderCreation>.Failure(new AppError(ErrorCode.Unauthorized, "Нет полномочий для выполнения действия."));
    }
}
