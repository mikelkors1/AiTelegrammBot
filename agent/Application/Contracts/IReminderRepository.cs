using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;

namespace ItmoBot.Application.Agent.Contracts;

public interface IReminderRepository
{
    Task<AgentStorageResult<ReminderCreation>> ExecuteConfirmedActionAsync(ActionOwner owner, Guid action, string expectedArguments, CancellationToken cancellationToken);
    Task<AgentStorageResult<StoredReminder>> GetReminderAsync(ActionOwner owner, Guid reminder, CancellationToken cancellationToken);
}
