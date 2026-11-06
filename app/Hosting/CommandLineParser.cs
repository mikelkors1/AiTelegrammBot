using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;

namespace ItmoBot.Hosting;

public sealed class CommandLineParser
{
    public Result<CommandLineOptions> Parse(IEnumerable<string> args)
    {
        var arguments = args.ToList();
        var envFile = ".env";
        var index = arguments.IndexOf("--env-file");

        if (index >= 0)
        {
            if (index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1])
                || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return new Result<CommandLineOptions>.Failure(new AppError(
                    ErrorCode.InvalidArguments, "--env-file: укажите путь к файлу."));
            }

            envFile = arguments[index + 1];
            arguments.RemoveRange(index, 2);
        }

        var command = ApplicationCommand.Run;

        if (arguments.Count == 1 && arguments[0] == "healthcheck")
        {
            command = ApplicationCommand.Healthcheck;
        }
        else if (arguments.Count == 1 && arguments[0] == "dbcheck")
        {
            command = ApplicationCommand.Dbcheck;
        }
        else if (arguments.Count == 1 && arguments[0] == "migrate")
        {
            command = ApplicationCommand.Migrate;
        }
        else if (arguments.Count > 0)
        {
            return new Result<CommandLineOptions>.Failure(new AppError(
                ErrorCode.InvalidArguments, "Использование: ItmoBot [healthcheck|dbcheck|migrate] [--env-file путь]."));
        }

        return new Result<CommandLineOptions>.Success(new CommandLineOptions(command, envFile));
    }
}
