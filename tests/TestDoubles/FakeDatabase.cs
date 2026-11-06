using ItmoBot.Application.Storage;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FakeDatabase : IDatabase
{
    public bool Disposed
    {
        get; private set;
    }
    public bool Fails
    {
        get; set;
    }

    public Task<Result> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Fails)
        {
            return Task.FromResult<Result>(new Result.Failure(new AppError(
                ErrorCode.DatabaseUnavailable, "База данных недоступна.", IsRetryable: true)));
        }

        return Task.FromResult<Result>(new Result.Success());
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
