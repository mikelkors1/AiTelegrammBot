using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.ResultTypes;

public abstract record class DatabaseMigrationResult
{
    private DatabaseMigrationResult()
    {
    }

    public sealed record class Success(int AppliedCount) : DatabaseMigrationResult;

    public sealed record class Failure(AppError Error) : DatabaseMigrationResult;
}
