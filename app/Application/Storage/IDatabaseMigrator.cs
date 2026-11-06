using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Storage;

public interface IDatabaseMigrator
{
    Task<Result<int>> ApplyAsync(CancellationToken cancellationToken);
}
