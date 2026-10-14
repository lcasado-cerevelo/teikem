namespace Teikem.Tests;

/// <summary>
/// Sección de logistica-db-update.sql desde su marca hasta el final, SIN la limpieza de datos «Fuera del catálogo los ZIP que ya no existen»
/// (2026-10-14 (b)): esa sección lleva DELETE a propósito (borra filas que el seed congelado vuelve a insertar), y las pruebas de las secciones
/// anteriores afirman que las suyas no borran nada.
/// </summary>
internal static class UpdateSqlSection
{
    private const string ZipCleanup = "2026-10-14 (b) — Fuera del catálogo los ZIP que ya no existen";

    public static string From(string sql, int start)
    {
        var cut = sql.IndexOf(ZipCleanup, start, StringComparison.Ordinal);
        if (cut < 0) return sql[start..];
        var header = sql.LastIndexOf("/*", cut, StringComparison.Ordinal); // el comentario de encabezado de esa sección
        return sql[start..header];
    }
}
