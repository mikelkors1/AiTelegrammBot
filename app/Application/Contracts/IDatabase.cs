using ItmoBot.Application.ResultTypes;

namespace ItmoBot.Application.Contracts;

public interface IDatabase : IAsyncDisposable
{
    Task<DatabaseCheckResult> CheckAsync(CancellationToken cancellationToken);
}
