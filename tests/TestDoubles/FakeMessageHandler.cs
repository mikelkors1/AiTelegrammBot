using ItmoBot.Application.Messaging;
using ItmoBot.Shared.Results;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FakeMessageHandler(Action onHandled) : IMessageHandler
{
    public Func<IncomingUpdate, CancellationToken, Task<Result<bool>>>? OnHandle
    {
        get; init;
    }

    public List<int> ReceivedUpdates { get; } = [];

    public Task<Result<bool>> HandleAsync(IncomingUpdate update, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReceivedUpdates.Add(update.Id);
        onHandled();
        return OnHandle is null
            ? Task.FromResult<Result<bool>>(new Result<bool>.Success(Value: true))
            : OnHandle(update, cancellationToken);
    }
}
