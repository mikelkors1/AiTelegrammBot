using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;

namespace ItmoBot.Application.Agent.Contracts;

public interface IPreparedActionRepository
{
    Task<AgentStorageResult<PreparedAction>> PrepareReminderAsync(ActionOwner owner, int update, ReminderPayload payload, CancellationToken cancellationToken);
    Task<AgentStorageResult<PreparedAction>> GetActionAsync(ActionOwner owner, Guid action, CancellationToken cancellationToken);
    Task<AgentStorageResult<PreparedAction>> ConfirmActionAsync(ActionOwner owner, Guid action, int confirmationUpdate, CancellationToken cancellationToken);
    Task<AgentStorageResult<PreparedAction>> CancelActionAsync(ActionOwner owner, Guid action, CancellationToken cancellationToken);
}
