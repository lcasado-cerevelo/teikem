namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 246 (Lote F del plan de Solutions) — de dónde nació una orden de compra (LookupCode 'PurchaseOrderOrigin'): TEIKEM (a mano, Reordenar un faltante y cualquier otro
/// camino), EXCEL (importada de un archivo: formato GENERIC o plantilla TEMPLATE) o QUICKBOOKS (importada de QuickBooks; nunca se exporta ni se marca). Un NULL en la base se
/// lee como TEIKEM.
/// </summary>
public static class PurchaseOrderOrigins
{
    public const string Domain = "PurchaseOrderOrigin";
    public const string Teikem = "TEIKEM";
    public const string Excel = "EXCEL";
    public const string QuickBooks = "QUICKBOOKS";

    public static readonly IReadOnlyList<string> All = new[] { Teikem, Excel, QuickBooks };

    /// <summary>El código canónico (sin distinguir mayúsculas ni espacios); vacío si no es válido.</summary>
    public static string Normalize(string? code)
    {
        var c = (code ?? string.Empty).Trim().ToUpperInvariant();
        return All.Contains(c) ? c : string.Empty;
    }

    /// <summary>El origen guardado o TEIKEM si es NULL (órdenes creadas antes del lote o por caminos sin origen).</summary>
    public static string OrDefault(string? code) => string.IsNullOrWhiteSpace(code) ? Teikem : code.Trim().ToUpperInvariant();

    /// <summary>El origen con que se crea una orden importada: QuickBooks → QUICKBOOKS; el formato genérico y la plantilla → EXCEL.</summary>
    public static string FromImportSource(string? source)
        => string.Equals(source?.Trim(), PurchaseOrderImportRules.SourceQuickBooks, StringComparison.OrdinalIgnoreCase) ? QuickBooks : Excel;

    public static string UnknownOrigin(string? code) => $"El origen «{code?.Trim()}» no es válido: use TEIKEM, EXCEL o QUICKBOOKS.";
}
