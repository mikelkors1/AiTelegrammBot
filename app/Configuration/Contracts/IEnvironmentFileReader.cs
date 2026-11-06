using ItmoBot.Shared.Results;

namespace ItmoBot.Configuration.Contracts;

public interface IEnvironmentFileReader
{
    Result<IReadOnlyDictionary<string, string>> Read(string path);
}
