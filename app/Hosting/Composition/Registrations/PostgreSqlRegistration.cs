using ItmoBot.Application.Agent.Contracts;
using ItmoBot.Application.Agent;
using ItmoBot.Application.Conversations;
using ItmoBot.Application.Storage;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.PostgreSql.Agent.Contracts;
using ItmoBot.Infrastructure.PostgreSql.Agent;
using ItmoBot.Infrastructure.PostgreSql.Contracts;
using ItmoBot.Infrastructure.PostgreSql.Conversations;
using ItmoBot.Infrastructure.PostgreSql.Migrations;
using ItmoBot.Infrastructure.PostgreSql;

namespace ItmoBot.Hosting.Composition.Registrations;

public sealed class PostgreSqlRegistration(Settings settings)
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<PostgresErrorMapper>();
        services.AddSingleton<ISqlMigrationCatalog, SqlMigrationCatalog>();
        services.AddScoped<IDatabase>(CreateDatabase);
        services.AddScoped<IDatabaseMigrator>(CreateMigrator);
        services.AddScoped<PostgresOperationExecutor>(CreateExecutor);
        services.AddScoped<ConversationStateStore>();
        services.AddScoped<ConversationTurnStore>();
        services.AddScoped<ConversationHistoryStore>();
        services.AddScoped<IConversationRepository, PostgresConversationRepository>();
        services.AddScoped<IAgentTransactionExecutor>(CreateAgentExecutor);
        RegisterAgentStorage(services);
    }

    private Npgsql.NpgsqlDataSource GetDataSource(IServiceProvider provider)
    {
        return ((Database)provider.GetRequiredService<IDatabase>()).DataSource;
    }

    private Database CreateDatabase(IServiceProvider provider)
    {
        return new Database(settings, provider.GetRequiredService<ILogger>());
    }

    private IDatabaseMigrator CreateMigrator(IServiceProvider provider)
    {
        return new PostgresDatabaseMigrator(
            GetDataSource(provider),
            provider.GetRequiredService<ISqlMigrationCatalog>(),
            provider.GetRequiredService<PostgresErrorMapper>(),
            provider.GetRequiredService<ILogger>());
    }

    private PostgresOperationExecutor CreateExecutor(IServiceProvider provider)
    {
        return new PostgresOperationExecutor(
            GetDataSource(provider),
            provider.GetRequiredService<PostgresErrorMapper>(),
            provider.GetRequiredService<ILogger>());
    }

    private IAgentTransactionExecutor CreateAgentExecutor(IServiceProvider provider)
    {
        return new AgentTransactionExecutor(GetDataSource(provider), provider.GetRequiredService<ILogger>());
    }

    private void RegisterAgentStorage(IServiceCollection services)
    {
        services.AddSingleton<ReminderPayloadHasher>();
        services.AddSingleton<PreparedActionPolicy>();
        services.AddScoped<AgentUserStore>();
        services.AddScoped<PreparedActionStore>();
        services.AddScoped<IAgentUserRepository, PostgresAgentUserRepository>();
        services.AddScoped<IPreparedActionRepository, PostgresPreparedActionRepository>();
        services.AddScoped<IReminderRepository, PostgresReminderRepository>();
        services.AddScoped<IAgentAuditRepository, PostgresAgentAuditRepository>();
    }
}
