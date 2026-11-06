using ItmoBot.Configuration.Contracts;
using ItmoBot.Configuration.Loading;
using ItmoBot.Infrastructure.Health;
using ItmoBot.Shared.Results;
using ItmoBot.Tools.Contracts;
using ItmoBot.Tools;

try
{
    if (args.Length > 0 && args[0] == "evaluate-routing")
    {
        return await new RoutingExperimentRunner().RunAsync(args);
    }

    IEnvironmentFileReader environmentFileReader = new EnvironmentFileReader();
    SettingsLoader settingsLoader = new SettingsLoader(environmentFileReader);
    ISecretConsoleInput secretInput = new SecretConsoleInput();
    EnvironmentPreparer environmentPreparer = new EnvironmentPreparer(environmentFileReader, settingsLoader, secretInput);
    IProcessRunner processRunner = new ProcessRunner();
    using ReadinessChecker checker = new ReadinessChecker();
    LocalDevelopmentRunner runner = new LocalDevelopmentRunner(environmentPreparer, settingsLoader,
        environmentFileReader, processRunner, checker);
    Result result = await runner.RunAsync(args);
    if (result is Result.Failure failure)
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
