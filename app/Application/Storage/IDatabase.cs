using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Storage;

public interface IDatabase : IAsyncDisposable
{
    Task<Result> CheckAsync(CancellationToken cancellationToken);
}
