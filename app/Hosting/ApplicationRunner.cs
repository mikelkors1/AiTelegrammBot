using ItmoBot.Application.Contracts;
using ItmoBot.Hosting.Contracts;
using ItmoBot.Application;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Configuration.Models;

namespace ItmoBot.Hosting;

public sealed class ApplicationRunner(
    Settings settings,
    IBotHost botHost,
    IDatabaseFactory databaseFactory,
    IReadinessChecker checker,
    IDatabaseMigrationRunner migrationRunner,
    ILogger logger) : IApplicationRunner
{
    public async Task<int> RunAsync(ApplicationCommand command)
    {
        try
        {
            if (command == ApplicationCommand.Healthcheck)
            {
                var checkedResult = await checker.CheckAsync(settings.HealthPort);
                if (checkedResult is ReadinessCheckResult.Failure healthFailure)
                {
                    Console.Error.WriteLine($"{healthFailure.Error.Code}: {healthFailure.Message}");
                    return 1;
                }

                Console.WriteLine("Приложение готово: polling работает, PostgreSQL отвечает на SELECT 1.");
                return 0;
            }

            if (command == ApplicationCommand.Dbcheck)
            {
                await using var database = databaseFactory.Create(settings, logger);
                var checkedResult = await database.CheckAsync(CancellationToken.None);
                if (checkedResult is DatabaseCheckResult.Failure databaseFailure)
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
                if (result is BotExecutionResult.Failure failure)
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
        var result = await migrationRunner.RunAsync(CancellationToken.None);
        if (result is DatabaseMigrationResult.Failure failure)
        {
            logger.LogError("{Code}: {Message}", failure.Error.Code, failure.Error.Message);
            return 1;
        }

        Console.WriteLine($"Миграции готовы: применено {((DatabaseMigrationResult.Success)result).AppliedCount}.");
        return 0;
    }

}
