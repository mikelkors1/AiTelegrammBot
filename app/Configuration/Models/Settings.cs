using ItmoBot.Configuration.Secrets;

namespace ItmoBot.Configuration.Models;

public sealed class Settings
{
    internal Settings(TelegramBotToken botToken, SecretValue postgresPassword, ProxyAddress? telegramProxy,
        string postgresHost, int postgresPort, string postgresDb, string postgresUser,
        LogLevel logLevel, int healthPort, LlmSettings llm, McpSettings mcp)
    {
        BotToken = botToken;
        PostgresPassword = postgresPassword;
        TelegramProxy = telegramProxy;
        PostgresHost = postgresHost;
        PostgresPort = postgresPort;
        PostgresDb = postgresDb;
        PostgresUser = postgresUser;
        LogLevel = logLevel;
        HealthPort = healthPort;
        Llm = llm;
        Mcp = mcp;
    }

    public McpSettings Mcp
    {
        get;
    }

    public LlmSettings Llm
    {
        get;
    }

    public TelegramBotToken BotToken
    {
        get;
    }
    public SecretValue PostgresPassword
    {
        get;
    }
    public ProxyAddress? TelegramProxy
    {
        get;
    }
    public string PostgresHost
    {
        get;
    }
    public int PostgresPort
    {
        get;
    }
    public string PostgresDb
    {
        get;
    }
    public string PostgresUser
    {
        get;
    }
    public LogLevel LogLevel
    {
        get;
    }
    public int HealthPort
    {
        get;
    }
}
