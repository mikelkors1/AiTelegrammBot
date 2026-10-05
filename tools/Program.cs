using ItmoBot.Application.Contracts;
using ItmoBot.Configuration.Contracts;
using ItmoBot.Configuration.Loading;
using ItmoBot.Infrastructure.Health;
using ItmoBot.Infrastructure.Logging;
using ItmoBot.Infrastructure.PostgreSql;
using ItmoBot.Tools.Contracts;
using ItmoBot.Tools;
using ItmoBot.Tools.ResultTypes;

try
{
    IEnvironmentFileReader environmentFileReader = new EnvironmentFileReader();
    ISettingsLoader settingsLoader = new SettingsLoader(environmentFileReader);
    ISecretConsoleInput secretInput = new SecretConsoleInput();
    IEnvironmentPreparer environmentPreparer = new EnvironmentPreparer(environmentFileReader, settingsLoader, secretInput);
    IProcessRunner processRunner = new ProcessRunner();
    IDatabaseFactory databaseFactory = new DatabaseFactory();
    ISecretRedactorFactory redactorFactory = new SecretRedactorFactory();
    using IReadinessChecker checker = new ReadinessChecker();
    ILocalDevelopmentRunner runner = new LocalDevelopmentRunner(environmentPreparer, settingsLoader,
        environmentFileReader, processRunner, databaseFactory, checker, redactorFactory);
    var result = await runner.RunAsync(args);
    if (result is LocalDevelopmentResult.Failure failure)
    {
        Console.Error.WriteLine($"{failure.Error.Code}: {failure.Error.Message}");
        return 1;
    }

    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Операция отменена.");
    return 130;
}
catch (Exception)
{
    Console.Error.WriteLine("UnexpectedFailure: непредвиденная ошибка локального запуска. Проверьте конфигурацию и доступ к файлам.");
    return 1;
}
