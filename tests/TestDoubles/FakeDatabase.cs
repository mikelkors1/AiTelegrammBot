using ItmoBot.Application.Contracts;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Shared.Errors;

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

    public Task<DatabaseCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Fails)
        {
            return Task.FromResult<DatabaseCheckResult>(new DatabaseCheckResult.Failure(new AppError(
                ErrorCode.DatabaseUnavailable, "База данных недоступна.", IsRetryable: true)));
        }

        return Task.FromResult<DatabaseCheckResult>(new DatabaseCheckResult.Success());
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
