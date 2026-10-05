using ItmoBot.Application.ResultTypes;

namespace ItmoBot.Application.Contracts;

public interface IDatabaseMigrator
{
    Task<DatabaseMigrationResult> ApplyAsync(CancellationToken cancellationToken);
}
