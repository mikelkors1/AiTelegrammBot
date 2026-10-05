using ItmoBot.Configuration.ResultTypes;

namespace ItmoBot.Configuration.Contracts;

public interface IEnvironmentFileReader
{
    EnvironmentReadResult Read(string path);
}
