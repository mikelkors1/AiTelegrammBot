using System.Net.Sockets;
using System.Net;
using ItmoBot.Configuration.Loading;
using ItmoBot.Configuration.Models;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Support;

internal sealed class TestValues
{
    public const string Token = "123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk";

    public Dictionary<string, string> Environment()
    {
        return new Dictionary<string, string>
        {
            ["BOT_TOKEN"] = Token,
            ["POSTGRES_PASSWORD"] = "db-secret",
            ["OPENROUTER_API_KEY"] = "test-only-openrouter-secret",
            ["LLM_API_BASE_URL"] = "https://openrouter.ai/api/v1/",
            ["LLM_MODEL"] = "qwen/qwen3.8-27b:free",
        };
    }

    public Settings Settings(IReadOnlyDictionary<string, string>? overrides = null)
    {
        var values = Environment();
        if (overrides is not null)
        {
            foreach (var entry in overrides)
            {
                values[entry.Key] = entry.Value;
            }
        }

        return Assert.IsType<Result<Settings>.Success>(new SettingsLoader(new EnvironmentFileReader()).Load("/nonexistent/env", values)).Value;
    }

    public int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return Port(((IPEndPoint)listener.LocalEndpoint).Port);
    }

    public int Port(int value)
    {
        return ConfigurationChecks.PortNumber(value);
    }

    public long Chat(long value)
    {
        return AgentChecks.ChatId(value);
    }

    public string Text(string value)
    {
        return TelegramChecks.TelegramText(value);
    }
}
