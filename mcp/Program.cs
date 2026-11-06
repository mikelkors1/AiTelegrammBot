using ItmoBot.Mcp.Hosting;
using Microsoft.Extensions.Hosting;

try
{
    var builder = Host.CreateApplicationBuilder(args);
    new McpServerComposition().Configure(builder);

    using var host = builder.Build();
    await host.RunAsync();
    return 0;
}
catch (ArgumentException)
{
    Console.Error.WriteLine("InvalidConfiguration: проверьте MCP_TIMEOUT_SECONDS, SCHEDULE_FILE, SCHEDULE_TIMEZONE, WEATHER_HTTP_* и настройки доступа к напоминаниям.");
    return 1;
}
catch (Exception)
{
    Console.Error.WriteLine("McpServerFailure: не удалось запустить MCP-сервер или продолжить его работу.");
    return 1;
}
