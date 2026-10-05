using ItmoBot.Application.Contracts;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.PostgreSql;
using ItmoBot.Infrastructure.PostgreSql.Contracts;
using ItmoBot.Infrastructure.PostgreSql.Conversations;
using ItmoBot.Infrastructure.PostgreSql.Migrations;

namespace ItmoBot.Hosting.Composition.Registrations;

public sealed class PostgreSqlRegistration(Settings settings)
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IDatabaseFactory, DatabaseFactory>();
        services.AddSingleton<IPostgresErrorMapper, PostgresErrorMapper>();
        services.AddSingleton<ISqlMigrationCatalog, SqlMigrationCatalog>();
        services.AddScoped<IDatabase>(CreateDatabase);
        services.AddScoped<IDatabaseMigrator>(CreateMigrator);
        services.AddScoped<IPostgresOperationExecutor>(CreateExecutor);
        services.AddScoped<IConversationStateStore, ConversationStateStore>();
        services.AddScoped<IConversationTurnStore, ConversationTurnStore>();
        services.AddScoped<IConversationHistoryStore, ConversationHistoryStore>();
        services.AddScoped<IConversationRepository, PostgresConversationRepository>();
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
            provider.GetRequiredService<IPostgresErrorMapper>(),
            provider.GetRequiredService<ILogger>());
    }

    private IPostgresOperationExecutor CreateExecutor(IServiceProvider provider)
    {
        return new PostgresOperationExecutor(
            GetDataSource(provider),
            provider.GetRequiredService<IPostgresErrorMapper>(),
            provider.GetRequiredService<ILogger>());
    }
}
