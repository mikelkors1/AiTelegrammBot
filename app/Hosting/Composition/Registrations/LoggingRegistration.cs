using ItmoBot.Application.Contracts;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Logging;
using Microsoft.Extensions.Logging.Console;

namespace ItmoBot.Hosting.Composition.Registrations;

public sealed class LoggingRegistration(Settings settings)
{
    public void Register(ILoggingBuilder logging, IServiceCollection services)
    {
        services.AddSingleton<ISecretRedactor>(new SecretRedactor(settings));
        services.AddSingleton<ILogger>(CreateLogger);
        logging.ClearProviders();
        logging.SetMinimumLevel(settings.LogLevel);
        logging.AddConsole(console =>
        {
            console.FormatterName = "safe";
        });
        logging.AddConsoleFormatter<SecretConsoleFormatter, ConsoleFormatterOptions>();
    }

    private ILogger CreateLogger(IServiceProvider provider)
    {
        return provider.GetRequiredService<ILoggerFactory>().CreateLogger("app");
    }
}
