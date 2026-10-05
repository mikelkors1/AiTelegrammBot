using ItmoBot.Application.ResultTypes;

namespace ItmoBot.Hosting.Contracts;

public interface IDatabaseMigrationRunner
{
    Task<DatabaseMigrationResult> RunAsync(CancellationToken cancellationToken);
}
