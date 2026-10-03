namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 23 — hoja de posición: el papel pegado en el rack con la lista de productos de la posición (SKU, nombre y código de
/// barras). La posición guarda cuándo se imprimió por última vez (WarehouseBin.SheetPrintedAtUtc) y cuándo cambió por última
/// vez su CONJUNTO de productos (WarehouseBin.SheetContentChangedAtUtc, lo fija InventoryLedger). El estado de la hoja es un
/// CÁLCULO sobre esas dos marcas y la existencia (no se guarda ni es catálogo: por eso no vive en LookupCode ni en StatusCode).
/// El filtro 'sheetStatus' del listado de posiciones aplica en SQL el mismo criterio (BinSheetTests lo compara fila por fila).
/// </summary>
public static class BinSheetRules
{
    /// <summary>Tope de posiciones por página de hojas (GET .../bin-sheets).</summary>
    public const int MaxSheetsPerPage = 200;
    /// <summary>Página por defecto de hojas.</summary>
    public const int DefaultSheetsPerPage = 50;
    /// <summary>Tope de posiciones por marca de impresión (POST .../bin-sheets/mark-printed).</summary>
    public const int MaxMarkPrinted = 500;

    /// <summary>
    /// Estado de la hoja de una posición:
    /// - STALE: se imprimió y su conjunto de productos cambió DESPUÉS (también si quedó vacía: la hoja pegada ya no sirve);
    /// - EMPTY: sin productos y sin cambios pendientes (nada que imprimir);
    /// - NEVER_PRINTED: con productos y sin impresión registrada (estado de arranque de toda posición con producto: sin backfill);
    /// - CURRENT: con productos, impresa y sin cambios del conjunto desde entonces.
    /// </summary>
    public static string Status(bool hasProducts, DateTime? printedAtUtc, DateTime? contentChangedAtUtc)
    {
        if (IsStale(printedAtUtc, contentChangedAtUtc)) return BinSheetStatuses.Stale;
        if (!hasProducts) return BinSheetStatuses.Empty;
        return printedAtUtc is null ? BinSheetStatuses.NeverPrinted : BinSheetStatuses.Current;
    }

    /// <summary>Impresa y con cambio del conjunto posterior a la impresión.</summary>
    public static bool IsStale(DateTime? printedAtUtc, DateTime? contentChangedAtUtc)
        => printedAtUtc is DateTime printed && contentChangedAtUtc is DateTime changed && changed > printed;

    /// <summary>Estados que piden imprimir (el contador 'staleCount'): STALE y NEVER_PRINTED.</summary>
    public static bool NeedsPrinting(string status) => status is BinSheetStatuses.Stale or BinSheetStatuses.NeverPrinted;

    /// <summary>
    /// ¿Cambió el conjunto de productos? Sí cuando el total en mano del producto en la posición (sumando lotes) cruza el cero:
    /// de 0 (o inexistente) a &gt; 0, o de &gt; 0 a 0. Subir o bajar sin cruzar el cero no cambia la hoja.
    /// </summary>
    public static bool ContentChanged(decimal totalBefore, decimal totalAfter) => totalBefore > 0m != totalAfter > 0m;

    /// <summary>
    /// Marca de impresión a guardar: el instante de los datos de la hoja (generatedAtUtc que devolvió GET .../bin-sheets) si
    /// llega, acotado a "ahora" (nunca en el futuro); si no, ahora. Nunca retrocede una marca ya guardada (idempotente: repetir la
    /// marca con el mismo instante no cambia nada; una hoja vieja marcada tarde no pisa una impresión más reciente).
    /// </summary>
    public static DateTime PrintedMark(DateTime nowUtc, DateTime? generatedAtUtc, DateTime? currentPrintedAtUtc)
    {
        var mark = generatedAtUtc is DateTime g && g < nowUtc ? g : nowUtc;
        return currentPrintedAtUtc is DateTime current && current > mark ? current : mark;
    }

    /// <summary>400 'Estado de hoja desconocido: '{x}'. Use NEVER_PRINTED, STALE, CURRENT o EMPTY.'</summary>
    public static string UnknownSheetStatus(string? code) => $"Estado de hoja desconocido: '{code}'. Use NEVER_PRINTED, STALE, CURRENT o EMPTY.";

    /// <summary>400 en 'take' de GET .../bin-sheets.</summary>
    public static readonly string TakeTooLarge = $"Se pueden pedir como máximo {MaxSheetsPerPage} hojas de posición por consulta; use skip para pedir las siguientes.";

    /// <summary>400 en 'binIds' de mark-printed sin posiciones.</summary>
    public const string MarkPrintedEmpty = "Indique al menos una posición para marcar su hoja como impresa.";

    /// <summary>400 en 'binIds' de mark-printed con demasiadas posiciones.</summary>
    public static readonly string MarkPrintedTooMany = $"Se pueden marcar como máximo {MaxMarkPrinted} posiciones por solicitud.";

    /// <summary>
    /// Filtro 'sheetStatus': códigos recortados y en mayúsculas, sin vacíos ni repetidos; admite la forma separada por comas.
    /// Sin valores → (null, null) (sin filtro); un código desconocido → (null, UnknownSheetStatus(código)).
    /// </summary>
    public static (IReadOnlySet<string>? Codes, string? Error) ParseStatuses(IEnumerable<string?>? raw)
    {
        if (raw is null) return (null, null);
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in raw)
        {
            if (string.IsNullOrWhiteSpace(r)) continue;
            foreach (var part in r.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var code = part.ToUpperInvariant();
                if (!BinSheetStatuses.All.Contains(code)) return (null, UnknownSheetStatus(part));
                set.Add(code);
            }
        }
        return set.Count == 0 ? (null, null) : (set, null);
    }
}

/// <summary>Lote 23 — estados de la hoja de posición (cálculo, no catálogo).</summary>
public static class BinSheetStatuses
{
    public const string NeverPrinted = "NEVER_PRINTED";
    public const string Stale = "STALE";
    public const string Current = "CURRENT";
    public const string Empty = "EMPTY";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { NeverPrinted, Stale, Current, Empty };
}
