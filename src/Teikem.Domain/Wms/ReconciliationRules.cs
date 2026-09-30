using System.Globalization;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>Descuadre Kárdex ↔ saldo de una clave o del total del producto (Key.WarehouseId = 0 en la fila del producto).</summary>
public sealed record ReconciliationMismatch(BalanceKey Key, decimal LedgerQty, decimal BalanceQty, bool ProductTotal)
{
    public string KindCode => ProductTotal ? DiscrepancyKinds.ProductTotal : DiscrepancyKinds.Balance;
}

/// <summary>Descuadre ya guardado (abierto o descartado) tal como lo ve el plan: tipo, clave y cifras de su última revisión.</summary>
public sealed record StoredDiscrepancy(int Id, string KindCode, BalanceKey Key, decimal LedgerQty, decimal BalanceQty);

/// <summary>Plan de una revisión: qué abrir, qué abiertos actualizar (con sus cifras nuevas) y qué abiertos se cerraron solos.</summary>
public sealed record ReconciliationPlan(IReadOnlyList<ReconciliationMismatch> ToOpen,
    IReadOnlyList<(int Id, ReconciliationMismatch Mismatch)> ToUpdate, IReadOnlyList<int> ToSelfCorrect);

/// <summary>
/// Lote 14 (P1) — reglas puras de la conciliación Kárdex ↔ saldo y de la resolución de descuadres (D5). Sin BD.
///
/// Comparación (la misma que TraceabilityService desde el Lote 6, extraída tal cual):
/// - por clave (producto, almacén, posición, lote): saldo reconstruido del Kárdex (To suma |Q|, From resta |Q|) ≠ QtyOnHand;
/// - por producto: Σ Quantity sin TRANSFER ≠ Σ QtyOnHand, SOLO si el producto no tiene ya un descuadre por clave (con todas sus
///   claves cuadradas el total cuadra por construcción; si alguna no cuadra, el total sería ruido).
/// Plan (D5): un descuadre nuevo se abre; uno abierto de la misma clave se actualiza; uno abierto que ya cuadra se cierra solo
/// (SELF_CORRECTED); uno descartado con LAS MISMAS cifras no se reabre (con cifras distintas, sí).
/// Resolución: REBUILD_BALANCE (el saldo toma el valor del Kárdex; solo BALANCE) o DISMISS (nota obligatoria).
/// Mensajes exactos (sección 3 del plan del Lote 14; el manual y la FAQ los citan).
/// </summary>
public static class ReconciliationRules
{
    /// <summary>Tope de productos de "Ejecutar conciliación" con productos elegidos.</summary>
    public const int MaxManualProducts = 200;
    /// <summary>Largo máximo de la nota de resolución (= ResolutionNotes NVARCHAR(500) y el comentario del historial).</summary>
    public const int MaxNotesLength = 500;

    public const string ActionRebuild = "REBUILD_BALANCE";
    public const string ActionDismiss = "DISMISS";

    public const string ManualTooManyProducts = "La conciliación manual admite como máximo 200 productos a la vez.";
    public const string ActionRequired = "Indique la acción: REBUILD_BALANCE (corregir el saldo) o DISMISS (descartar).";
    public const string DismissNoteRequired = "Escriba una nota que explique por qué se descarta el descuadre.";
    public const string NotesTooLong = "La nota admite como máximo 500 caracteres.";
    public const string AlreadyClosed = "El descuadre ya está cerrado; solo se consulta.";
    public const string ProductTotalNotRebuildable =
        "Este descuadre es del total del producto; no se corrige por posición. Corrija los descuadres por posición o descártelo con una nota.";
    /// <summary>Comentario del historial cuando una revisión encuentra cuadrado un descuadre abierto.</summary>
    public const string SelfCorrectedComment = "La revisión encontró el saldo cuadrado con el Kárdex.";
    /// <summary>Comentario del historial cuando se pidió corregir y el saldo ya cuadraba.</summary>
    public const string AlreadyBalancedComment = "El saldo ya cuadraba con el Kárdex al corregir.";

    /// <summary>'El Kárdex da {ledger} para {sku} en {bin}, menos que lo reservado ({reserved}); libere la reserva antes de corregir el saldo.' (409)</summary>
    public static string LedgerBelowReserved(decimal ledger, string sku, string bin, decimal reserved)
        => $"El Kárdex da {Qty(ledger)} para {sku} en {bin}, menos que lo reservado ({Qty(reserved)}); libere la reserva antes de corregir el saldo.";

    /// <summary>'El Kárdex da un saldo negativo ({ledger}) para {sku} en {bin}; revise los movimientos antes de corregir el saldo.' (409)</summary>
    public static string LedgerNegative(decimal ledger, string sku, string bin)
        => $"El Kárdex da un saldo negativo ({Qty(ledger)}) para {sku} en {bin}; revise los movimientos antes de corregir el saldo.";

    /// <summary>Comentario del historial al abrir: 'Kárdex {l}, saldo {b}.'.</summary>
    public static string OpenedComment(decimal ledger, decimal balance) => $"Kárdex {Qty(ledger)}, saldo {Qty(balance)}.";

    /// <summary>Diferencia mostrada: saldo − Kárdex (positiva = el saldo dice más de lo que dan los movimientos).</summary>
    public static decimal Difference(decimal ledgerQty, decimal balanceQty) => balanceQty - ledgerQty;

    private static string Qty(decimal q) => q.ToString("0.###", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- comparación

    /// <summary>
    /// Descuadres a partir del saldo reconstruido por clave, el saldo guardado por clave, el neto por producto (sin TRANSFER) y
    /// el en mano por producto. Orden estable: producto, clave antes que total, almacén, posición y lote.
    /// </summary>
    public static IReadOnlyList<ReconciliationMismatch> Compare(IReadOnlyDictionary<BalanceKey, decimal> rebuilt,
        IReadOnlyDictionary<BalanceKey, decimal> balanceByKey, IReadOnlyDictionary<int, decimal> netByProduct,
        IReadOnlyDictionary<int, decimal> onHandByProduct)
    {
        var mismatches = new List<ReconciliationMismatch>();
        foreach (var key in rebuilt.Keys.Union(balanceByKey.Keys))
        {
            var ledgerQty = rebuilt.GetValueOrDefault(key);
            var balanceQty = balanceByKey.GetValueOrDefault(key);
            if (ledgerQty != balanceQty) mismatches.Add(new ReconciliationMismatch(key, ledgerQty, balanceQty, false));
        }
        var productsWithKeyMismatch = mismatches.Select(m => m.Key.ProductId).ToHashSet();
        foreach (var p in netByProduct.Keys.Union(onHandByProduct.Keys))
        {
            if (productsWithKeyMismatch.Contains(p)) continue;
            var net = netByProduct.GetValueOrDefault(p);
            var onHand = onHandByProduct.GetValueOrDefault(p);
            if (net != onHand) mismatches.Add(new ReconciliationMismatch(new BalanceKey(p, 0, null, null), net, onHand, true));
        }
        return mismatches.OrderBy(m => m.Key.ProductId).ThenBy(m => m.ProductTotal).ThenBy(m => m.Key.WarehouseId)
            .ThenBy(m => m.Key.BinId).ThenBy(m => m.Key.LotId).ToList();
    }

    // ---------------------------------------------------------------- plan

    /// <summary>
    /// Plan de una revisión (D5).
    /// - openExisting: los descuadres ABIERTOS de los productos revisados.
    /// - dismissed: los DESCARTADOS de esos productos (basta el más reciente por clave; se comparan las cifras).
    /// - mismatches: lo que la revisión encontró descuadrado.
    /// - checkedProducts: productos revisados (NULL = todos los del tenant). Un abierto de un producto NO revisado no se toca.
    /// Un abierto de total de producto con descuadres por clave del mismo producto se deja como está (Compare omite el total
    /// mientras haya claves descuadradas: no significa que cuadre).
    /// </summary>
    public static ReconciliationPlan Plan(IReadOnlyList<StoredDiscrepancy> openExisting, IReadOnlyList<StoredDiscrepancy> dismissed,
        IReadOnlyList<ReconciliationMismatch> mismatches, IReadOnlySet<int>? checkedProducts)
    {
        var open = new Dictionary<(string Kind, BalanceKey Key), StoredDiscrepancy>();
        foreach (var o in openExisting) open.TryAdd((o.KindCode, o.Key), o);
        var dismissedByKey = dismissed.GroupBy(d => (d.KindCode, d.Key)).ToDictionary(g => g.Key, g => g.ToList());

        var toOpen = new List<ReconciliationMismatch>();
        var toUpdate = new List<(int, ReconciliationMismatch)>();
        var seen = new HashSet<(string, BalanceKey)>();
        foreach (var m in mismatches)
        {
            var k = (m.KindCode, m.Key);
            if (!seen.Add(k)) continue;
            if (open.TryGetValue(k, out var existing)) { toUpdate.Add((existing.Id, m)); continue; }
            if (dismissedByKey.TryGetValue(k, out var gone) && gone.Any(d => d.LedgerQty == m.LedgerQty && d.BalanceQty == m.BalanceQty)) continue;
            toOpen.Add(m);
        }

        var productsWithKeyMismatch = mismatches.Where(m => !m.ProductTotal).Select(m => m.Key.ProductId).ToHashSet();
        var toClose = new List<int>();
        foreach (var o in open.Values)
        {
            if (seen.Contains((o.KindCode, o.Key))) continue;
            if (checkedProducts is not null && !checkedProducts.Contains(o.Key.ProductId)) continue;
            if (o.KindCode == DiscrepancyKinds.ProductTotal && productsWithKeyMismatch.Contains(o.Key.ProductId)) continue;
            toClose.Add(o.Id);
        }
        return new ReconciliationPlan(toOpen, toUpdate, toClose.OrderBy(i => i).ToList());
    }

    // ---------------------------------------------------------------- resolución

    /// <summary>
    /// Validación de forma de la solicitud (400): acción REBUILD_BALANCE o DISMISS (sin distinguir mayúsculas), nota de a lo sumo
    /// 500 caracteres y obligatoria al descartar. Devuelve la acción normalizada, la nota recortada (vacía = NULL) y los errores
    /// por campo.
    /// </summary>
    public static (string? Action, string? Notes, IDictionary<string, string[]> Errors) ValidateResolveRequest(string? action, string? notes)
    {
        var errors = new Dictionary<string, string[]>();
        var code = action?.Trim().ToUpperInvariant();
        if (code is not (ActionRebuild or ActionDismiss))
        {
            errors["action"] = new[] { ActionRequired };
            code = null;
        }
        var note = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (note is { Length: > MaxNotesLength }) errors["notes"] = new[] { NotesTooLong };
        else if (code == ActionDismiss && note is null) errors["notes"] = new[] { DismissNoteRequired };
        return (code, note, errors);
    }

    /// <summary>
    /// Reglas de estatus (422): un descuadre cerrado solo se consulta; el total del producto no se corrige por posición.
    /// Devuelve el mensaje o null.
    /// </summary>
    public static string? ResolveBlocker(bool isClosed, string kindCode, string action)
    {
        if (isClosed) return AlreadyClosed;
        if (action == ActionRebuild && kindCode == DiscrepancyKinds.ProductTotal) return ProductTotalNotRebuildable;
        return null;
    }

    /// <summary>
    /// ¿Se puede poner el saldo de la clave en lo que da el Kárdex? (409): el Kárdex no puede dar negativo ni menos que lo
    /// reservado (CK_StockBalance_Qty). Devuelve el mensaje o null.
    /// </summary>
    public static string? RebuildCheck(decimal ledgerQty, decimal reserved, string sku, string bin)
    {
        if (ledgerQty < 0m) return LedgerNegative(ledgerQty, sku, bin);
        if (ledgerQty < reserved) return LedgerBelowReserved(ledgerQty, sku, bin, reserved);
        return null;
    }
}
