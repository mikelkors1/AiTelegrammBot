using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace ItmoBot.Infrastructure.Logging;

public sealed class SecretConsoleFormatter(SecretRedactor redactor) : ConsoleFormatter("safe")
{
    public override void Write<TState>(in LogEntry<TState> entry, IExternalScopeProvider? scopes, TextWriter writer)
    {
        var text = entry.Formatter(entry.State, null);
        if (entry.Exception is not null)
        {
            text += " Тип ошибки: " + entry.Exception.GetType().Name;
        }

        writer.WriteLine($"{DateTimeOffset.Now:O} {entry.LogLevel} {entry.Category}: {redactor.Redact(text)}");
    }
}
