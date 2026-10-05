using ItmoBot.Application.Contracts;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FakeDatabaseMigrator(AppError? error = null) : IDatabaseMigrator
{
    public Task<DatabaseMigrationResult> ApplyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (error is not null)
        {
            return Task.FromResult<DatabaseMigrationResult>(new DatabaseMigrationResult.Failure(error));
        }

        return Task.FromResult<DatabaseMigrationResult>(new DatabaseMigrationResult.Success(0));
    }
}
