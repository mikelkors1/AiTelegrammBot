using ItmoBot.Application.Storage;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FakeDatabaseMigrator(AppError? error = null) : IDatabaseMigrator
{
    public int ApplyCalls
    {
        get; private set;
    }

    public Task<Result<int>> ApplyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyCalls++;
        if (error is not null)
        {
            return Task.FromResult<Result<int>>(new Result<int>.Failure(error));
        }

        return Task.FromResult<Result<int>>(new Result<int>.Success(0));
    }
}
