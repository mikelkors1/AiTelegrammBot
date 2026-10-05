using ItmoBot.Configuration.ValueObjects;
using ItmoBot.Shared.ValueObjects;

namespace ItmoBot.Configuration.Models;

public sealed class Settings
{
    internal Settings(TelegramBotToken botToken, SecretValue postgresPassword, ProxyAddress? telegramProxy,
        PostgresHost postgresHost, PortNumber postgresPort, DatabaseName postgresDb, DatabaseUser postgresUser,
        LogLevel logLevel, PortNumber healthPort, LlmSettings llm)
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
    public PostgresHost PostgresHost
    {
        get;
    }
    public PortNumber PostgresPort
    {
        get;
    }
    public DatabaseName PostgresDb
    {
        get;
    }
    public DatabaseUser PostgresUser
    {
        get;
    }
    public LogLevel LogLevel
    {
        get;
    }
    public PortNumber HealthPort
    {
        get;
    }
}
