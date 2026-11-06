using System.Security.Cryptography;
using System.Text;
using ItmoBot.Configuration.Contracts;
using ItmoBot.Configuration.Loading;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Health;
using ItmoBot.Infrastructure.Logging;
using ItmoBot.Infrastructure.PostgreSql;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Tools.Contracts;

namespace ItmoBot.Tools;

public sealed class LocalDevelopmentRunner(
    EnvironmentPreparer environmentPreparer,
    SettingsLoader settingsLoader,
    IEnvironmentFileReader environmentFileReader,
    IProcessRunner processRunner,
    ReadinessChecker checker)
{
    public async Task<Result> RunAsync(string[] args)
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
            Result prepared = environmentPreparer.Prepare();
            if (prepared is Result.Failure preparationFailure)
            {
                return new Result.Failure(preparationFailure.Error);
            }
        }

        Result<Settings> loaded = settingsLoader.Load();
        if (loaded is Result<Settings>.Failure settingsFailure)
        {
            return new Result.Failure(settingsFailure.Error);
        }

        var settings = ((Result<Settings>.Success)loaded).Value;
        var redactor = new SecretRedactor(settings);
        if (action == "health")
        {
            var readiness = await checker.CheckAsync(settings.HealthPort);
            if (readiness is Result.Failure readinessFailure)
            {
                return new Result.Failure(readinessFailure.Error);
            }

            Console.WriteLine("Приложение готово: polling работает, PostgreSQL отвечает на SELECT 1.");
            return new Result.Success();
        }

        Result docker = await processRunner.RunAsync("docker", ["info"], "Проверка Docker Desktop/Engine", redactor, quiet: true);
        if (docker is Result.Failure dockerFailure)
        {
            return new Result.Failure(dockerFailure.Error);
        }

        Result composeVersion = await processRunner.RunAsync("docker", ["compose", "version"], "Проверка Docker Compose v2", redactor);
        if (composeVersion is Result.Failure composeFailure)
        {
            return new Result.Failure(composeFailure.Error);
        }

        var project = "itmo-local-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..10].ToLowerInvariant();
        string[] compose = ["compose", "--project-name", project, "--project-directory", root,
            "--env-file", Path.Combine(root, ".env"), "-f", "compose.yaml", "-f", "compose.local.yaml"];
        Result<IReadOnlyDictionary<string, string>> read = environmentFileReader.Read(".env");
        if (read is Result<IReadOnlyDictionary<string, string>>.Failure readFailure)
        {
            return new Result.Failure(readFailure.Error);
        }

        var environment = new Dictionary<string, string>(((Result<IReadOnlyDictionary<string, string>>.Success)read).Value);
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

        if (settings.PostgresHost is not ("127.0.0.1" or "localhost"))
        {
            return Failure(ErrorCode.InvalidConfiguration, "Для локального запуска POSTGRES_HOST должен быть 127.0.0.1.");
        }

        Console.WriteLine("Запускаем PostgreSQL и ждём готовности…");
        Result started = await processRunner.RunAsync("docker", [.. compose, "up", "-d", "--wait", "--wait-timeout", "90", "db"],
            "Запуск PostgreSQL: проверьте порт и доступ к Docker Hub", redactor, environment);
        if (started is Result.Failure startFailure)
        {
            return new Result.Failure(startFailure.Error);
        }

        await using (var database = new Database(settings))
        {
            var checkedResult = await database.CheckAsync(CancellationToken.None);
            if (checkedResult is Result.Failure databaseFailure)
            {
                return new Result.Failure(databaseFailure.Error);
            }
        }

        Console.WriteLine("Окружение готово, PostgreSQL отвечает на SELECT 1.");
        if (args.Contains("--setup-only"))
        {
            Console.WriteLine("Откройте ItmoBot.sln в IDE. Запуск: dotnet run --project app.");
            return new Result.Success();
        }

        return Map(await processRunner.RunAsync("dotnet",
            ["run", "--project", "app", "--", "--env-file", Path.Combine(root, ".env")],
            "Запуск бота", redactor, interactive: true));
    }

    private Result Failure(ErrorCode code, string message)
    {
        return new Result.Failure(new AppError(code, message));
    }

    private Result Map(Result result)
    {
        if (result is Result.Failure failure)
        {
            return new Result.Failure(failure.Error);
        }

        return new Result.Success();
    }
}
