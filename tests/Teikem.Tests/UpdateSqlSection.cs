namespace Teikem.Tests;

/// <summary>
/// Sección de logistica-db-update.sql desde su marca hasta el final, SIN las secciones que borran o quitan algo a propósito, porque las
/// pruebas de las secciones anteriores afirman que las suyas no borran nada:
/// - la limpieza de datos «Fuera del catálogo los ZIP que ya no existen» (2026-10-14 (b)): lleva DELETE (borra filas que el seed congelado
///   vuelve a insertar);
/// - la del Lote 243 (2026-11-24): vuelve a crear CK_NotificationRuleRecipient_Kind para admitir CONTEXT, así que lleva DROP CONSTRAINT;
/// - la del Lote 248 (2026-11-27): vuelve a crear CK_ClientNotificationSetting_Kind para admitir DOCUMENT_EXPIRING, así que lleva DROP CONSTRAINT.
/// </summary>
internal static class UpdateSqlSection
{
    private static readonly string[] Cuts = ["2026-10-14 (b)", "2026-11-24 — Lote 243", "2026-11-27 — Lote 248"];

    public static string From(string sql, int start)
    {
        var cut = -1;
        foreach (var marker in Cuts)
        {
            var at = sql.IndexOf(marker, start, StringComparison.Ordinal);
            if (at > start && (cut < 0 || at < cut)) cut = at;
        }
        if (cut < 0) return sql[start..];
        var header = sql.LastIndexOf("/*", cut, StringComparison.Ordinal); // el comentario de encabezado de esa sección
        return sql[start..header];
    }
}
