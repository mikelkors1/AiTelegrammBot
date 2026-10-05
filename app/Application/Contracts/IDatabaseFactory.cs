using ItmoBot.Configuration.Models;

namespace ItmoBot.Application.Contracts;

public interface IDatabaseFactory
{
    IDatabase Create(Settings settings, ILogger? logger = null);
}
