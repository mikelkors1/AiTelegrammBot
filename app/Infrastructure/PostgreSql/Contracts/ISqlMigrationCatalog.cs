using ItmoBot.Infrastructure.PostgreSql.Migrations;

namespace ItmoBot.Infrastructure.PostgreSql.Contracts;

public interface ISqlMigrationCatalog
{
    IReadOnlyList<SqlMigration> Read();
}
