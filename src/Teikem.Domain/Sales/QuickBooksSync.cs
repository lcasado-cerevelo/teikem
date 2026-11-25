namespace Teikem.Domain.Sales;

/// <summary>
/// Lote 246 (Lote F del plan de Solutions) — estados de un documento frente a QuickBooks Desktop, GENÉRICOS para cualquier documento que se pase por archivo IIF
/// (hoy las órdenes de compra; el Lote E los puede usar para las facturas complementarias). Ventas (Lote 225) no se toca.
/// </summary>
public static class QuickBooksSyncStates
{
    /// <summary>Por pasar: elegible, nunca exportado ni marcado.</summary>
    public const string Pending = "PENDING";
    /// <summary>Salió en un archivo y falta confirmar si entró («exportada, por confirmar»).</summary>
    public const string Exported = "EXPORTED";
    /// <summary>Marcado como ya en QuickBooks (a mano o al confirmar un archivo).</summary>
    public const string InQuickBooks = "IN_QUICKBOOKS";
    /// <summary>Vino de QuickBooks: nunca se exporta ni se marca.</summary>
    public const string FromQuickBooks = "FROM_QUICKBOOKS";
    /// <summary>No se puede pasar (por su estatus): nunca exportado ni marcado.</summary>
    public const string NotExportable = "NOT_EXPORTABLE";

    public static readonly IReadOnlyList<string> All = new[] { Pending, Exported, InQuickBooks, FromQuickBooks, NotExportable };
}

/// <summary>Lote 246 — la ÚNICA función que calcula el estado frente a QuickBooks; la usan el DTO, la lista (sus predicados la imitan), la exportación y la fuente de datos.</summary>
public static class QuickBooksSync
{
    /// <summary>
    /// Estado con prioridad fija: 1) vino de QuickBooks → FROM_QUICKBOOKS; 2) marcado → IN_QUICKBOOKS; 3) exportado → EXPORTED (aunque después dejara de ser elegible,
    /// p. ej. cancelado: sigue por confirmar); 4) elegible → PENDING; 5) si no, NOT_EXPORTABLE.
    /// </summary>
    public static string State(bool fromQuickBooks, bool eligible, DateTime? exportedAt, DateTime? quickBooksAt)
    {
        if (fromQuickBooks) return QuickBooksSyncStates.FromQuickBooks;
        if (quickBooksAt is not null) return QuickBooksSyncStates.InQuickBooks;
        if (exportedAt is not null) return QuickBooksSyncStates.Exported;
        return eligible ? QuickBooksSyncStates.Pending : QuickBooksSyncStates.NotExportable;
    }
}
