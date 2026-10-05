using ItmoBot.Configuration.ResultTypes;

namespace ItmoBot.Configuration.Contracts;

public interface ISettingsLoader
{
    SettingsLoadResult Load(string envFile = ".env", IReadOnlyDictionary<string, string>? environment = null);
}
