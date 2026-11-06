using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Health;
using ItmoBot.Infrastructure.PostgreSql;
using ItmoBot.Shared.Results;

namespace ItmoBot.Hosting;

public sealed class ApplicationRunner(
    Settings settings,
    BotHost botHost,
    ReadinessChecker checker,
    ILogger logger)
{
    public async Task<int> RunAsync(ApplicationCommand command)
    {
        try
        {
            if (command == ApplicationCommand.Healthcheck)
            {
                var checkedResult = await checker.CheckAsync(settings.HealthPort);
                if (checkedResult is Result.Failure healthFailure)
                {
                    Console.Error.WriteLine($"{healthFailure.Error.Code}: {healthFailure.Message}");
                    return 1;
                }

                Console.WriteLine("Приложение готово: polling работает, PostgreSQL отвечает на SELECT 1.");
                return 0;
            }

            if (command == ApplicationCommand.Dbcheck)
            {
                await using var database = new Database(settings, logger);
                var checkedResult = await database.CheckAsync(CancellationToken.None);
                if (checkedResult is Result.Failure databaseFailure)
                {
                    logger.LogError("{Code}: {Message}", databaseFailure.Error.Code, databaseFailure.Message);
                    return 1;
                }

                Console.WriteLine("PostgreSQL отвечает на SELECT 1.");
                return 0;
            }

            if (command == ApplicationCommand.Migrate)
            {
                return await RunMigrationsAsync();
            }

            using var stopping = new CancellationTokenSource();
            ConsoleCancelEventHandler stop = (_, e) =>
            {
                e.Cancel = true;
                stopping.Cancel();
            };
            Console.CancelKeyPress += stop;
            try
            {
                var result = await botHost.RunAsync(stopping.Token);
                if (result is Result.Failure failure)
                {
                    logger.LogError("{Code}: {Message}", failure.Error.Code, failure.Message);
                    return 1;
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
            }
            finally
            {
                Console.CancelKeyPress -= stop;
            }

            logger.LogInformation("Бот остановлен.");
            return 0;
        }
        catch (Exception error)
        {
            logger.LogError(error, "Непредвиденная ошибка приложения. Проверьте конфигурацию и логи.");
            return 1;
        }
    }

    private async Task<int> RunMigrationsAsync()
    {
        var result = await botHost.MigrateAsync(CancellationToken.None);
        if (result is Result<int>.Failure failure)
        {
            logger.LogError("{Code}: {Message}", failure.Error.Code, failure.Error.Message);
            return 1;
        }

        Console.WriteLine($"Миграции готовы: применено {((Result<int>.Success)result).Value}.");
        return 0;
    }

}
