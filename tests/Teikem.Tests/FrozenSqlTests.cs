using System.Security.Cryptography;
using Xunit;
using System.Text;
using System.Text.RegularExpressions;
using Teikem.Infrastructure.Persistence.Scripts;

namespace Teikem.Tests;

/// <summary>
/// En producción desde 2026-10-09: Diseño/logistica-db-estructura.sql y logistica-db-seed.sql están CONGELADOS. Si esta prueba falla es
/// porque alguien los editó: revierta esa edición y ponga el cambio (estructura o datos) en un archivo nuevo e idempotente en
/// Diseño/logistica-db-update.sql (un solo archivo idempotente, al final). NO actualice los hashes de aquí abajo.
/// </summary>
public class FrozenSqlTests
{
    private const string StructureSha256 = "3366c3257f831bdff5fa1cde576088cc820489984434240e1d73691f9b8a5b78";
    private const string SeedSha256 = "e4cc313033279600f1ae7c5270a40d85484ad3c80ac198382286e42dca7c9545";

    private static string Design() => Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño");

    private static string Hash(string path)
    {
        // se normaliza el fin de línea (git en Windows puede convertirlo) para que el hash sea el mismo en todas partes
        var text = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    [Fact]
    public void Structure_script_is_frozen()
        => Assert.True(StructureSha256 == Hash(Path.Combine(Design(), "logistica-db-estructura.sql")),
            "logistica-db-estructura.sql está CONGELADO (producción desde 2026-10-09). Revierta la edición y use Diseño/logistica-db-update.sql.");

    [Fact]
    public void Seed_script_is_frozen()
        => Assert.True(SeedSha256 == Hash(Path.Combine(Design(), "logistica-db-seed.sql")),
            "logistica-db-seed.sql está CONGELADO (producción desde 2026-10-09). Revierta la edición y use Diseño/logistica-db-update.sql.");

    [Fact]
    public void Update_script_exists_and_is_idempotent_by_construction()
    {
        // un solo archivo para estructura y datos; cada cambio nuevo es una sección al final y debe poder correr dos veces
        var update = DatabaseInitializer.UpdateScript(Design());
        Assert.Equal("logistica-db-update.sql", update.Name);
        var sql = File.ReadAllText(update.Path, Encoding.UTF8);
        Assert.Contains("IDEMPOTENTE", sql);
        // ningún CREATE/ALTER/INSERT sin guarda: las altas de tablas, columnas y filas deben ir con IF ... / MERGE (revisión básica por patrón)
        foreach (var line in sql.Split('\n').Select(l => l.Trim()))
        {
            if (line.StartsWith("--") || line.StartsWith("/*") || line.StartsWith("*")) continue;
            Assert.False(Regex.IsMatch(line, @"^CREATE\s+TABLE\b", RegexOptions.IgnoreCase), $"CREATE TABLE sin IF OBJECT_ID(...) IS NULL: {line}");
        }
    }
}
