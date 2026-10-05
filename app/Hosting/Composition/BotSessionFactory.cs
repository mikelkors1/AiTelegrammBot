using ItmoBot.Application.Contracts;
using ItmoBot.Hosting.Contracts;

namespace ItmoBot.Hosting.Composition;

public sealed class BotSessionFactory(IServiceScopeFactory scopeFactory) : IBotSessionFactory
{
    public async ValueTask<IBotSession> CreateAsync()
    {
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            return new BotSession(scope, scope.ServiceProvider.GetRequiredService<IBotApplication>(),
                scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>());
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }
}
