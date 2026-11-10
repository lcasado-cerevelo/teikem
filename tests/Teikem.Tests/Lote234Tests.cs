using Teikem.Domain.Files;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 234 — (1) el encabezado con que se sirve un archivo solo lleva ASCII (un nombre como «Diseño sin título.png» tumbaba GET /files/{id} con 500 y la foto salía
/// rota); (2) las ofertas activas en borrador se publican una sola vez en el script de cambios.
/// </summary>
public sealed class Lote234Tests
{
    [Theory]
    [InlineData("foto.jpg", "inline; filename=\"foto.jpg\"")]
    [InlineData("Diseño sin título.png", "inline; filename=\"Dise_o sin t_tulo.png\"; filename*=UTF-8''Dise%C3%B1o%20sin%20t%C3%ADtulo.png")]
    [InlineData("año\u202f2026.webp", "inline; filename=\"a_o_2026.webp\"; filename*=UTF-8''a%C3%B1o%E2%80%AF2026.webp")]
    [InlineData("", "inline; filename=\"archivo\"")]
    public void The_inline_disposition_is_always_ascii(string name, string expected)
    {
        var header = StoredFileRules.InlineDisposition(name);
        Assert.Equal(expected, header);
        Assert.All(header, c => Assert.InRange(c, ' ', '~'));   // Kestrel rechaza cualquier otro carácter en un encabezado
    }

    [Fact]
    public void A_name_kept_by_SafeFileName_with_accents_still_gives_a_valid_header()
    {
        var stored = StoredFileRules.SafeFileName("C:\\fotos\\Niño «promoción».JPG", "image/jpeg");
        Assert.Equal("Niño promoción.jpg", stored);
        Assert.All(StoredFileRules.InlineDisposition(stored), c => Assert.InRange(c, ' ', '~'));
    }

    [Fact]
    public void Update_sql_publishes_the_active_draft_offers_once_with_a_mark()
    {
        var sql = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-11-10 — Lote 234: las ofertas ya no tienen «borrador»", StringComparison.Ordinal);
        Assert.True(start > sql.IndexOf("2026-11-10 — Lote 233: parámetro de la compañía", StringComparison.Ordinal));
        var section = UpdateSqlSection.From(sql, start);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.AppliedMigration WHERE Code = 'L234_OFFER_NO_DRAFT')", section);
        Assert.Contains("UPDATE dbo.Offer SET IsPublished = 1, UpdatedAtUtc = SYSUTCDATETIME() WHERE IsActive = 1 AND IsPublished = 0;", section);
        Assert.Contains("INSERT INTO dbo.AppliedMigration (Code) VALUES ('L234_OFFER_NO_DRAFT');", section);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PriceList", section, StringComparison.Ordinal);   // las listas de precios siguen con borrador
    }
}
