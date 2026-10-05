using ItmoBot.Hosting.ResultTypes;

namespace ItmoBot.Hosting.Contracts;

public interface ICommandLineParser
{
    CommandLineParseResult Parse(IEnumerable<string> args);
}
