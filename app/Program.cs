using ItmoBot.Configuration.Contracts;
using ItmoBot.Configuration.Loading;
using ItmoBot.Configuration.Models;
using ItmoBot.Hosting.Composition;
using ItmoBot.Hosting;
using ItmoBot.Shared.Results;

try
{
    var parser = new CommandLineParser();
    Result<CommandLineOptions> parsed = parser.Parse(args);

    if (parsed is Result<CommandLineOptions>.Failure argumentFailure)
    {
        Console.Error.WriteLine($"{argumentFailure.Error.Code}: {argumentFailure.Message}");
        return 1;
    }

    var options = ((Result<CommandLineOptions>.Success)parsed).Value;
    IEnvironmentFileReader environmentFileReader = new EnvironmentFileReader();
    SettingsLoader settingsLoader = new SettingsLoader(environmentFileReader);
    Result<Settings> loaded = settingsLoader.Load(options.EnvFile);

    if (loaded is Result<Settings>.Failure settingsFailure)
    {
        Console.Error.WriteLine($"{settingsFailure.Error.Code}: {settingsFailure.Message}");
        return 1;
    }

    var settings = ((Result<Settings>.Success)loaded).Value;
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
    var composition = new ApplicationComposition(settings);
    composition.Configure(builder);

    using var host = builder.Build();
    return await host.Services.GetRequiredService<ApplicationRunner>().RunAsync(options.Command);
}
catch (Exception)
{
    Console.Error.WriteLine("UnexpectedFailure: непредвиденная ошибка запуска. Проверьте конфигурацию и доступ к файлам.");
    return 1;
}
