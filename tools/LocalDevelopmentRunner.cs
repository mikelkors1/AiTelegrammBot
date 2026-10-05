using ItmoBot.Application.Contracts;
using ItmoBot.Configuration.Contracts;
using ItmoBot.Tools.Contracts;
using System.Security.Cryptography;
using System.Text;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Configuration.ResultTypes;
using ItmoBot.Shared.Errors;
using ItmoBot.Tools.ResultTypes;

namespace ItmoBot.Tools;

public sealed class LocalDevelopmentRunner(
    IEnvironmentPreparer environmentPreparer,
    ISettingsLoader settingsLoader,
    IEnvironmentFileReader environmentFileReader,
    IProcessRunner processRunner,
    IDatabaseFactory databaseFactory,
    IReadinessChecker checker,
    ISecretRedactorFactory redactorFactory) : ILocalDevelopmentRunner
{
    public async Task<LocalDevelopmentResult> RunAsync(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(root, "ItmoBot.sln")))
        {
            return Failure(ErrorCode.InvalidArguments, "Запускайте утилиту из корня проекта.");
        }

        var action = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "up";
        if (args.Count(a => !a.StartsWith('-')) > 1
            || args.Any(a => a != action && a != "--setup-only")
            || action is not ("up" or "stop" or "status" or "logs" or "health")
            || (action != "up" && args.Contains("--setup-only")))
        {
            return Failure(ErrorCode.InvalidArguments, "Использование: start [up|stop|status|logs|health] [--setup-only].");
        }

        if (action == "up")
        {
            var prepared = environmentPreparer.Prepare();
            if (prepared is EnvironmentPreparationResult.Failure preparationFailure)
            {
                return new LocalDevelopmentResult.Failure(preparationFailure.Error);
            }
        }

        var loaded = settingsLoader.Load();
        if (loaded is SettingsLoadResult.Failure settingsFailure)
        {
            return new LocalDevelopmentResult.Failure(settingsFailure.Error);
        }

        var settings = ((SettingsLoadResult.Success)loaded).Settings;
        var redactor = redactorFactory.Create(settings);
        if (action == "health")
        {
            var readiness = await checker.CheckAsync(settings.HealthPort);
            if (readiness is ReadinessCheckResult.Failure readinessFailure)
            {
                return new LocalDevelopmentResult.Failure(readinessFailure.Error);
            }

            Console.WriteLine("Приложение готово: polling работает, PostgreSQL отвечает на SELECT 1.");
            return new LocalDevelopmentResult.Success();
        }

        var docker = await processRunner.RunAsync("docker", ["info"], "Проверка Docker Desktop/Engine", redactor, quiet: true);
        if (docker is CommandExecutionResult.Failure dockerFailure)
        {
            return new LocalDevelopmentResult.Failure(dockerFailure.Error);
        }

        var composeVersion = await processRunner.RunAsync("docker", ["compose", "version"], "Проверка Docker Compose v2", redactor);
        if (composeVersion is CommandExecutionResult.Failure composeFailure)
        {
            return new LocalDevelopmentResult.Failure(composeFailure.Error);
        }

        var project = "itmo-local-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..10].ToLowerInvariant();
        string[] compose = ["compose", "--project-name", project, "--project-directory", root,
            "--env-file", Path.Combine(root, ".env"), "-f", "compose.yaml", "-f", "compose.local.yaml"];
        var read = environmentFileReader.Read(".env");
        if (read is EnvironmentReadResult.Failure readFailure)
        {
            return new LocalDevelopmentResult.Failure(readFailure.Error);
        }

        var environment = new Dictionary<string, string>(((EnvironmentReadResult.Success)read).Values);
        foreach (var key in environment.Keys.ToArray())
        {
            if (Environment.GetEnvironmentVariable(key) is { } value)
            {
                environment[key] = value;
            }
        }

        if (action != "up")
        {
            string[] command = action switch
            {
                "stop" => ["stop", "db"],
                "status" => ["ps"],
                _ => ["logs", "--tail", "100", "db"],
            };
            return Map(await processRunner.RunAsync("docker", [.. compose, .. command], "Управление локальной БД", redactor, environment));
        }

        if (settings.PostgresHost.Value is not ("127.0.0.1" or "localhost"))
        {
            return Failure(ErrorCode.InvalidConfiguration, "Для локального запуска POSTGRES_HOST должен быть 127.0.0.1.");
        }

        Console.WriteLine("Запускаем PostgreSQL и ждём готовности…");
        var started = await processRunner.RunAsync("docker", [.. compose, "up", "-d", "--wait", "--wait-timeout", "90", "db"],
            "Запуск PostgreSQL: проверьте порт и доступ к Docker Hub", redactor, environment);
        if (started is CommandExecutionResult.Failure startFailure)
        {
            return new LocalDevelopmentResult.Failure(startFailure.Error);
        }

        await using (var database = databaseFactory.Create(settings))
        {
            var checkedResult = await database.CheckAsync(CancellationToken.None);
            if (checkedResult is DatabaseCheckResult.Failure databaseFailure)
            {
                return new LocalDevelopmentResult.Failure(databaseFailure.Error);
            }
        }

        Console.WriteLine("Окружение готово, PostgreSQL отвечает на SELECT 1.");
        if (args.Contains("--setup-only"))
        {
            Console.WriteLine("Откройте ItmoBot.sln в IDE. Запуск: dotnet run --project app.");
            return new LocalDevelopmentResult.Success();
        }

        return Map(await processRunner.RunAsync("dotnet",
            ["run", "--project", "app", "--", "--env-file", Path.Combine(root, ".env")],
            "Запуск бота", redactor, interactive: true));
    }

    private LocalDevelopmentResult Failure(ErrorCode code, string message)
    {
        return new LocalDevelopmentResult.Failure(new AppError(code, message));
    }

    private LocalDevelopmentResult Map(CommandExecutionResult result)
    {
        if (result is CommandExecutionResult.Failure failure)
        {
            return new LocalDevelopmentResult.Failure(failure.Error);
        }

        return new LocalDevelopmentResult.Success();
    }
}
