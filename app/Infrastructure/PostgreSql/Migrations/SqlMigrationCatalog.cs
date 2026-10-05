using System.Security.Cryptography;
using System.Text;
using ItmoBot.Infrastructure.PostgreSql.Contracts;

namespace ItmoBot.Infrastructure.PostgreSql.Migrations;

public sealed class SqlMigrationCatalog : ISqlMigrationCatalog
{
    public IReadOnlyList<SqlMigration> Read()
    {
        const string prefix = "ItmoBot.Migrations.";
        var assembly = typeof(SqlMigrationCatalog).Assembly;
        var migrations = new List<SqlMigration>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var name = resource[prefix.Length..];
            var separator = name.IndexOf('_');
            if (separator < 1 || !int.TryParse(name[..separator], out var version) || version <= 0)
            {
                throw new InvalidOperationException("Некорректное имя встроенной миграции.");
            }

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            // Keep checksums stable across Windows and Unix checkouts.
            var sql = reader.ReadToEnd().ReplaceLineEndings("\n");
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
            migrations.Add(new SqlMigration(version, name, sql, checksum));
        }

        var ordered = migrations.OrderBy(migration => migration.Version).ToArray();
        if (ordered.Length == 0 || ordered.Select(migration => migration.Version).Distinct().Count() != ordered.Length)
        {
            throw new InvalidOperationException("Каталог миграций пуст или содержит повторяющиеся версии.");
        }

        return Array.AsReadOnly(ordered);
    }
}
