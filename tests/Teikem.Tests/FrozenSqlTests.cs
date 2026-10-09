using System.Security.Cryptography;
using Xunit;
using System.Text;
using System.Text.RegularExpressions;
using Teikem.Infrastructure.Persistence.Scripts;

namespace Teikem.Tests;

/// <summary>
/// En producción desde 2026-10-09: Diseño/logistica-db-estructura.sql y logistica-db-seed.sql están CONGELADOS. Si esta prueba falla es
/// porque alguien los editó: revierta esa edición y ponga el cambio (estructura o datos) en un archivo nuevo e idempotente en
/// Diseño/cambios/NNNN-descripcion.sql (ver Diseño/cambios/README.md). NO actualice los hashes de aquí abajo.
/// </summary>
public class FrozenSqlTests
{
    private const string StructureSha256 = "017b97b6cec46ac10745f5d134d00a91961ac250e64727553c5a4d388c322897";
    private const string SeedSha256 = "27bc44cda6c50f0052d6c0a2c3aba0fe944432fc2fd12eed7896b55008e1170f";

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
            "logistica-db-estructura.sql está CONGELADO (producción desde 2026-10-09). Revierta la edición y use Diseño/cambios/NNNN-*.sql.");

    [Fact]
    public void Seed_script_is_frozen()
        => Assert.True(SeedSha256 == Hash(Path.Combine(Design(), "logistica-db-seed.sql")),
            "logistica-db-seed.sql está CONGELADO (producción desde 2026-10-09). Revierta la edición y use Diseño/cambios/NNNN-*.sql.");

    [Fact]
    public void Change_scripts_are_numbered_and_ordered()
    {
        var scripts = DatabaseInitializer.ChangeScripts(Design());
        foreach (var s in scripts)
            Assert.Matches(new Regex(@"^cambios/\d{4}-[a-z0-9-]+\.sql$"), s.Name);
        var names = scripts.Select(s => s.Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
