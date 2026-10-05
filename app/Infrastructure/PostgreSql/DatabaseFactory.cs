using ItmoBot.Application.Contracts;
using ItmoBot.Configuration.Models;

namespace ItmoBot.Infrastructure.PostgreSql;

public sealed class DatabaseFactory : IDatabaseFactory
{
    public IDatabase Create(Settings settings, ILogger? logger = null)
    {
        return new Database(settings, logger);
    }
}
