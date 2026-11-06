using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Messaging;

public interface IMessageHandler
{
    Task<Result<bool>> HandleAsync(IncomingUpdate update, CancellationToken cancellationToken);
}
