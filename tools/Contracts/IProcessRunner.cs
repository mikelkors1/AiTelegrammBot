using ItmoBot.Infrastructure.Logging;
using ItmoBot.Shared.Results;

namespace ItmoBot.Tools.Contracts;

public interface IProcessRunner
{
    Task<Result> RunAsync(
        string executable, string[] arguments, string description, SecretRedactor redactor,
        IReadOnlyDictionary<string, string>? environment = null, bool quiet = false, bool interactive = false);
}
