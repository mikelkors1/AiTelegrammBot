using ItmoBot.Application.Contracts;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Hosting.Contracts;

namespace ItmoBot.Hosting.Composition;

public sealed class BotSession : IBotSession
{
    private readonly AsyncServiceScope scope;
    private readonly IDatabaseMigrator migrator;

    public BotSession(AsyncServiceScope scope, IBotApplication application, IDatabaseMigrator migrator)
    {
        this.scope = scope;
        Application = application;
        this.migrator = migrator;
    }

    public IBotApplication Application
    {
        get;
    }

    public Task<DatabaseMigrationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        return migrator.ApplyAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
    }
}
