using Teikem.Domain.Clients;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 6 — Numeración de documentos WMS (pura). Un contador por tenant (ClientId NULL) en dbo.NumberSequence:
/// REC-##### (RECEIPT), CC-##### (CYCLECOUNT), XD-##### (CROSSDOCK) y PO-##### (PURCHASE); Lote 27 (Rentas): REN-##### (RENTAL) y
/// DRN-##### (RENTALRETURN). La recolección NO tiene patrón
/// propio: usa NumberingRules.PackBatchPattern con el contador PACKBATCH (D10), así su número ES el PackBatchNumber de la orden.
/// </summary>
public static class WmsNumbering
{
    public const string ReceiptPattern = "REC-#####";
    public const string CycleCountPattern = "CC-#####";
    public const string CrossDockPattern = "XD-#####";
    public const string PurchaseOrderPattern = "PO-#####";
    public const string RentalPattern = "REN-#####";
    public const string RentalReturnPattern = "DRN-#####";

    public static string PatternFor(string kind) => kind switch
    {
        NumberKinds.Receipt => ReceiptPattern,
        NumberKinds.CycleCount => CycleCountPattern,
        NumberKinds.CrossDock => CrossDockPattern,
        NumberKinds.Purchase => PurchaseOrderPattern,
        NumberKinds.Rental => RentalPattern,
        NumberKinds.RentalReturn => RentalReturnPattern,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Tipo de número WMS desconocido; use RECEIPT, CYCLECOUNT, CROSSDOCK, PURCHASE, RENTAL o RENTALRETURN."),
    };

    /// <summary>Dibuja el número del contador con el patrón del tipo (p. ej. RECEIPT, 7 → 'REC-00007').</summary>
    public static string Format(string kind, long value) => NumberFormat.Resolve(PatternFor(kind), value);
}
