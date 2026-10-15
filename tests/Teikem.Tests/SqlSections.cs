namespace Teikem.Tests;

/// <summary>Una sección de logistica-db-update.sql: desde su marca hasta el encabezado de la siguiente (cada lote prueba SOLO la suya).</summary>
internal static class SqlSections
{
    public static string From(string sql, int start)
    {
        var next = sql.IndexOf("/* ---", start + 10, StringComparison.Ordinal);
        return next < 0 ? sql[start..] : sql[start..next];
    }
}
