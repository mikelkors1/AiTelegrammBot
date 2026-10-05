using ItmoBot.Application.ValueObjects;
using ItmoBot.Application.Contracts;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.Models;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FakeMessageHandler(Action onHandled) : IMessageHandler
{
    public List<int> ReceivedUpdates { get; } = [];

    public Task<MessageHandlingResult> HandleAsync(IncomingUpdate update, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReceivedUpdates.Add(update.Id.Value);
        onHandled();
        return Task.FromResult<MessageHandlingResult>(new MessageHandlingResult.Success(WasHandled: true));
    }
}
