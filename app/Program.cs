using ItmoBot.Configuration.Contracts;
using ItmoBot.Hosting.Contracts;
using ItmoBot.Hosting.Composition;
using ItmoBot.Configuration.Loading;
using ItmoBot.Configuration.ResultTypes;
using ItmoBot.Hosting;
using ItmoBot.Hosting.ResultTypes;

try
{
    ICommandLineParser parser = new CommandLineParser();
    var parsed = parser.Parse(args);

    if (parsed is CommandLineParseResult.Failure argumentFailure)
    {
        Console.Error.WriteLine($"{argumentFailure.Error.Code}: {argumentFailure.Message}");
        return 1;
    }

    var options = ((CommandLineParseResult.Success)parsed).Options;
    IEnvironmentFileReader environmentFileReader = new EnvironmentFileReader();
    ISettingsLoader settingsLoader = new SettingsLoader(environmentFileReader);
    var loaded = settingsLoader.Load(options.EnvFile);

    if (loaded is SettingsLoadResult.Failure settingsFailure)
    {
        Console.Error.WriteLine($"{settingsFailure.Error.Code}: {settingsFailure.Message}");
        return 1;
    }

    var settings = ((SettingsLoadResult.Success)loaded).Settings;
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
    var composition = new ApplicationComposition(settings);
    composition.Configure(builder);

    using var host = builder.Build();
    return await host.Services.GetRequiredService<IApplicationRunner>().RunAsync(options.Command);
}
catch (Exception)
{
    Console.Error.WriteLine("UnexpectedFailure: непредвиденная ошибка запуска. Проверьте конфигурацию и доступ к файлам.");
    return 1;
}
