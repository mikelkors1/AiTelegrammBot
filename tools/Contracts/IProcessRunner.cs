using ItmoBot.Tools.ResultTypes;
using ItmoBot.Application.Contracts;

namespace ItmoBot.Tools.Contracts;

public interface IProcessRunner
{
    Task<CommandExecutionResult> RunAsync(
        string executable, string[] arguments, string description, ISecretRedactor redactor,
        IReadOnlyDictionary<string, string>? environment = null, bool quiet = false, bool interactive = false);
}
