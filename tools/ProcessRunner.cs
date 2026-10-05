using System.ComponentModel;
using System.Diagnostics;
using ItmoBot.Application.Contracts;
using ItmoBot.Tools.Contracts;
using ItmoBot.Shared.Errors;
using ItmoBot.Tools.ResultTypes;

namespace ItmoBot.Tools;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<CommandExecutionResult> RunAsync(
        string executable, string[] arguments, string description, ISecretRedactor redactor,
        IReadOnlyDictionary<string, string>? environment = null, bool quiet = false, bool interactive = false)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = !interactive,
            RedirectStandardError = !interactive,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var item in environment)
            {
                info.Environment[item.Key] = item.Value;
            }
        }

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return new CommandExecutionResult.Failure(new AppError(
                    ErrorCode.ProcessUnavailable, $"{description}: не удалось создать процесс."));
            }

            async Task ReadAsync(StreamReader reader)
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (!quiet)
                    {
                        Console.WriteLine(redactor.Redact(line));
                    }
                }
            }

            var output = interactive ? Task.CompletedTask : ReadAsync(process.StandardOutput);
            var errors = interactive ? Task.CompletedTask : ReadAsync(process.StandardError);
            await Task.WhenAll(output, errors, process.WaitForExitAsync());
            if (process.ExitCode != 0)
            {
                var advice = quiet ? "Проверьте установку и запуск программы." : "Проверьте вывод выше.";
                return new CommandExecutionResult.Failure(new AppError(
                    ErrorCode.ProcessFailed, $"{description}: команда завершилась с кодом {process.ExitCode}. {advice}"));
            }

            return new CommandExecutionResult.Success();
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new CommandExecutionResult.Failure(new AppError(
                ErrorCode.ProcessUnavailable, $"{description}: программа недоступна. Проверьте установку и PATH."));
        }
    }
}
