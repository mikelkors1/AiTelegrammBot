using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.Models;

namespace ItmoBot.Application.Contracts;

public interface IMessageHandler
{
    Task<MessageHandlingResult> HandleAsync(IncomingUpdate update, CancellationToken cancellationToken);
}
