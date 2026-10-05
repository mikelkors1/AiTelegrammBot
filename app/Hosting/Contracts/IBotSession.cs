using ItmoBot.Application.Contracts;
using ItmoBot.Application.ResultTypes;

namespace ItmoBot.Hosting.Contracts;

public interface IBotSession : IAsyncDisposable
{
    Task<DatabaseMigrationResult> InitializeAsync(CancellationToken cancellationToken);

    IBotApplication Application
    {
        get;
    }
}
