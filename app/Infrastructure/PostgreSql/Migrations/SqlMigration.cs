namespace ItmoBot.Infrastructure.PostgreSql.Migrations;

public sealed record class SqlMigration(int Version, string Name, string Sql, string Checksum);
