using ItmoBot.Application.Contracts;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Hosting.Contracts;

namespace ItmoBot.Hosting.Composition;

public sealed class DatabaseMigrationRunner(IServiceScopeFactory scopeFactory) : IDatabaseMigrationRunner
{
    public async Task<DatabaseMigrationResult> RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
        return await migrator.ApplyAsync(cancellationToken);
    }
}
