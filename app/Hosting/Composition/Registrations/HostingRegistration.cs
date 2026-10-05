using ItmoBot.Hosting.Contracts;

namespace ItmoBot.Hosting.Composition.Registrations;

public sealed class HostingRegistration
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IBotSessionFactory, BotSessionFactory>();
        services.AddSingleton<IBotHost, BotHost>();
        services.AddSingleton<IDatabaseMigrationRunner, DatabaseMigrationRunner>();
        services.AddSingleton<IApplicationRunner, ApplicationRunner>();
    }
}
