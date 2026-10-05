using ItmoBot.Infrastructure.PostgreSql.Contracts;
using ItmoBot.Infrastructure.PostgreSql.Migrations;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FixedMigrationCatalog(IReadOnlyList<SqlMigration> migrations) : ISqlMigrationCatalog
{
    public IReadOnlyList<SqlMigration> Read()
    {
        return migrations;
    }
}
