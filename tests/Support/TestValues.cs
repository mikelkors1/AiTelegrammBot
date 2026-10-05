using System.Net;
using System.Net.Sockets;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Configuration.Loading;
using ItmoBot.Configuration.Models;
using ItmoBot.Configuration.ResultTypes;
using ItmoBot.Shared.ValueObjects;
using Xunit;
using ChatId = ItmoBot.Application.ValueObjects.ChatId;

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

        return Assert.IsType<SettingsLoadResult.Success>(new SettingsLoader(new EnvironmentFileReader()).Load("/nonexistent/env", values)).Settings;
    }

    public PortNumber FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return Port(((IPEndPoint)listener.LocalEndpoint).Port);
    }

    public PortNumber Port(int value)
    {
        return new PortNumber(value);
    }

    public ChatId Chat(long value)
    {
        return new ChatId(value);
    }

    public TelegramText Text(string value)
    {
        return new TelegramText(value);
    }
}
