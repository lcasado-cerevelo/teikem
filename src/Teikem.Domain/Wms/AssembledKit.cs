using System.Globalization;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 232 — constantes del KIT ARMADO (producto con inventario propio). Viven aquí (y no en CatalogDomains) para que el lote se pueda pasar solo a otra rama,
/// como KitCodes. El tipo de producto es <see cref="ProductTypes.AssembledKit"/>.
/// </summary>
public static class AssembledKitCodes
{
    /// <summary>EntityType del documento de armado (ARM-#####) y de sus líneas; también es el origen (RefEntityType) de sus movimientos.</summary>
    public const string AssemblyEntityType = "KIT_ASSEMBLY";
    /// <summary>EntityType del documento de desarmado (DES-#####) y de sus líneas; origen de sus movimientos.</summary>
    public const string DisassemblyEntityType = "KIT_DISASSEMBLY";
    /// <summary>Contador por compañía (ClientId NULL) del armado: ARM-#####. El número es también el del lote del kit.</summary>
    public const string AssemblyNumberKind = "KITASSEMBLY";
    /// <summary>Contador por compañía (ClientId NULL) del desarmado: DES-#####.</summary>
    public const string DisassemblyNumberKind = "KITDISASSEMBLY";
    public const string AssemblyPattern = "ARM-#####";
    public const string DisassemblyPattern = "DES-#####";
    /// <summary>Tope de kits por armado o desarmado.</summary>
    public const int MaxKitsPerOperation = 100_000;
}

/// <summary>
/// Lote 232 — un ARMADO de kits (ARM-#####): en una sola transacción salieron del inventario las piezas de <see cref="Quantity"/> kits y entraron esos kits a la
/// posición destino. Es un documento sin estatus (de una sola pasada, como el ajuste múltiple). Si el kit se rastrea por lote, el armado creó el lote del kit
/// (<see cref="KitLotId"/>, con el mismo número) y sus líneas RECUERDAN el lote y la cantidad de cada pieza. <see cref="DisassembledQty"/> = cuántos de estos kits se
/// desarmaron después. El costo es la suma del costo de las piezas al armar (según el método de valuación de la compañía): <see cref="TotalCost"/> y por kit
/// <see cref="UnitCost"/>; null si ninguna pieza tenía costo. Es una capa de costo del kit (ValuationService).
/// </summary>
[AuditEntity(AssembledKitCodes.AssemblyEntityType)]
public class KitAssembly : ITenantScoped
{
    public int KitAssemblyId { get; set; }
    [NotAudited] public Guid PublicId { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    /// <summary>ARM-##### (contador KITASSEMBLY por compañía).</summary>
    public string Number { get; set; } = string.Empty;
    public int KitProductId { get; set; }
    public int WarehouseId { get; set; }
    /// <summary>Posición donde entraron los kits.</summary>
    public int ToBinId { get; set; }
    /// <summary>Lote del kit que nació con este armado (null si el kit no se rastrea por lote).</summary>
    public int? KitLotId { get; set; }
    /// <summary>Kits armados (entero).</summary>
    public decimal Quantity { get; set; }
    /// <summary>Kits de este armado que ya se desarmaron.</summary>
    public decimal DisassembledQty { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }
    public string? Notes { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public ICollection<KitAssemblyLine> Lines { get; set; } = new List<KitAssemblyLine>();
}

/// <summary>
/// Línea de un armado: de qué posición y de qué lote salió cuánta cantidad de una pieza (un solo lote por pieza en cada armado; puede haber varias posiciones del
/// mismo lote). <see cref="ReturnedQty"/> = lo que ya volvió por desarmados. <see cref="UnitCost"/> = costo unitario con que salió la pieza (al desarmar vuelve a ese costo).
/// </summary>
[AuditEntity(AssembledKitCodes.AssemblyEntityType)]
public class KitAssemblyLine : ITenantScoped
{
    public int KitAssemblyLineId { get; set; }
    public int TenantId { get; set; }
    public int KitAssemblyId { get; set; }
    public int ComponentProductId { get; set; }
    public int? LotId { get; set; }
    public int FromBinId { get; set; }
    /// <summary>Cantidad de la pieza por kit (la composición al armar).</summary>
    public decimal QtyPerKit { get; set; }
    public decimal Quantity { get; set; }
    public decimal ReturnedQty { get; set; }
    public decimal? UnitCost { get; set; }
    /// <summary>Movimiento del libro (salida) que generó esta línea.</summary>
    public long? InventoryTransactionId { get; set; }
}

/// <summary>
/// Lote 232 — un DESARMADO (DES-#####) de kits de UN armado concreto: salieron <see cref="Quantity"/> kits disponibles (no reservados) y cada pieza volvió a su
/// mismo lote, a la posición indicada o a la original, al costo con que salió. Documento sin estatus.
/// </summary>
[AuditEntity(AssembledKitCodes.DisassemblyEntityType)]
public class KitDisassembly : ITenantScoped
{
    public int KitDisassemblyId { get; set; }
    [NotAudited] public Guid PublicId { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    /// <summary>DES-##### (contador KITDISASSEMBLY por compañía).</summary>
    public string Number { get; set; } = string.Empty;
    public int KitAssemblyId { get; set; }
    public int KitProductId { get; set; }
    /// <summary>Almacén donde estaban los kits (y donde vuelven las piezas).</summary>
    public int WarehouseId { get; set; }
    public int? KitLotId { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public ICollection<KitDisassemblyLine> Lines { get; set; } = new List<KitDisassemblyLine>();
}

/// <summary>Línea de un desarmado: cuánto de una pieza volvió, a qué lote y posición, y a qué costo (el de la línea del armado). Es una capa de costo de la pieza.</summary>
[AuditEntity(AssembledKitCodes.DisassemblyEntityType)]
public class KitDisassemblyLine : ITenantScoped
{
    public int KitDisassemblyLineId { get; set; }
    public int TenantId { get; set; }
    public int KitDisassemblyId { get; set; }
    public int KitAssemblyLineId { get; set; }
    public int ComponentProductId { get; set; }
    public int? LotId { get; set; }
    public int ToBinId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? UnitCost { get; set; }
    public long? InventoryTransactionId { get; set; }
}

/// <summary>Una toma de una pieza para armar: posición, lote y cantidad.</summary>
public sealed record KitPieceTake(int BinId, int? LotId, decimal Quantity);

/// <summary>Lote 232 — reglas puras del kit armado: mensajes exactos, rastreo, vencimiento del lote del kit y cuántos kits alcanzan. Sin BD.</summary>
public static class AssembledKitRules
{
    public static string NotAssembledKit(string sku) => $"El producto {sku} no es un kit armado.";
    public static string PieceIsSerial(string sku) => $"La pieza {sku} se rastrea por serie: un kit armado no lleva piezas con serie en esta versión.";
    public static string PieceIsKit(string sku) => $"{sku} es un kit: un kit armado no lleva otro kit como pieza.";
    public static string TrackingDecidedByPieces(string expected)
        => $"El rastreo de un kit armado lo deciden sus piezas: debe ser {expected} (LOT si alguna pieza se rastrea por lote; NONE si ninguna).";
    public const string TrackingFixed = "El rastreo de un kit armado lo deciden sus piezas: no se cambia aquí (cambie la composición mientras el kit no tenga armados, o cree un kit nuevo).";
    public const string OwnerFixed = "El dueño de un kit armado es el de sus piezas: no se cambia (cree un kit nuevo con las piezas del otro dueño).";
    public static string CompositionLocked(string sku) => $"El kit {sku} ya tiene armados, movimientos o existencia: su composición no se puede cambiar. Cree un kit nuevo.";
    public static string NoPieces(string sku) => $"El kit {sku} no tiene piezas: agregue su composición antes de armar.";
    public const string KitQuantityRequired = "Indique cuántos kits.";
    public const string KitQuantityInvalid = "La cantidad de kits debe ser un número entero mayor que cero.";
    public static string KitQuantityTooLarge() => $"Se arman o desarman como máximo {AssembledKitCodes.MaxKitsPerOperation.ToString("N0", CultureInfo.InvariantCulture)} kits a la vez.";
    public const string ToBinRequired = "Indique la posición donde quedan los kits.";
    public const string PieceNotInKit = "El producto indicado no es pieza de este kit.";
    public static string PieceRepeated(string sku) => $"La pieza {sku} está repetida en la selección.";
    public static string LotOnNonLotPiece(string sku) => $"La pieza {sku} no se rastrea por lote: no indique lote.";
    public static string BinNotAllowed(string bin) => $"La posición {bin} es de cuarentena, en renta o de cross-dock: no se usa para armar ni desarmar kits.";
    public const string AssembleOnCreateNeedsBin = "Para armar kits al crear el producto indique la posición donde quedan.";

    /// <summary>'El lote {lot} de {sku} solo alcanza para {k} kit(s)…' (409 insufficient_stock): un solo lote por pieza en cada armado.</summary>
    public static string LotNotEnough(string lot, string sku, decimal available, decimal qtyPerKit)
    {
        var kits = KitsCoveredBy(available, qtyPerKit);
        var a = InventoryRules.FormatQty(Math.Max(0m, available));
        var q = InventoryRules.FormatQty(qtyPerKit);
        return kits == 0
            ? $"El lote {lot} de {sku} no alcanza para ningún kit: hay {a} disponible y cada kit lleva {q}."
            : $"El lote {lot} de {sku} solo alcanza para {kits} kit(s): hay {a} disponible y cada kit lleva {q}. Arme {kits} con ese lote y el resto en otro armado con otro lote.";
    }

    /// <summary>'No hay suficiente {sku} disponible en {wh} para {n} kit(s)…' (409 insufficient_stock).</summary>
    public static string PieceNotEnough(string sku, string where, decimal available, decimal needed, int kits, decimal qtyPerKit)
        => $"No hay suficiente {sku} disponible en {where} para {kits} kit(s): hay {InventoryRules.FormatQty(Math.Max(0m, available))} y se necesitan {InventoryRules.FormatQty(needed)}; alcanza para {KitsCoveredBy(available, qtyPerKit)}.";

    public static string AssemblyRemaining(string number, decimal remaining, decimal requested)
        => $"Del armado {number} quedan {InventoryRules.FormatQty(remaining)} kit(s) por desarmar; pidió {InventoryRules.FormatQty(requested)}.";

    public static string KitsNotAvailable(string number, string where, decimal available, decimal requested)
        => $"Solo hay {InventoryRules.FormatQty(Math.Max(0m, available))} kit(s) disponible(s) del armado {number} en {where} y pidió desarmar {InventoryRules.FormatQty(requested)}. Los kits reservados por una orden o ya despachados no se desarman.";

    public const string PiecesBinRequired = "Indique la posición donde vuelven las piezas: los kits están en otro almacén que el del armado.";

    /// <summary>Cantidad de kits: obligatoria, entera, &gt; 0 y hasta el tope. Mensaje o null.</summary>
    public static string? ValidateKitQuantity(decimal? quantity)
    {
        if (quantity is not decimal q) return KitQuantityRequired;
        if (q <= 0m || decimal.Truncate(q) != q) return KitQuantityInvalid;
        return q > AssembledKitCodes.MaxKitsPerOperation ? KitQuantityTooLarge() : null;
    }

    /// <summary>Rastreo del kit armado según sus piezas: LOT si alguna pieza se rastrea por lote; NONE si ninguna. (Las piezas con serie se rechazan antes.)</summary>
    public static string DecideTracking(IEnumerable<string?> pieceTrackings)
        => pieceTrackings.Any(t => string.Equals(t, TrackingTypes.Lot, StringComparison.OrdinalIgnoreCase)) ? TrackingTypes.Lot : TrackingTypes.None;

    /// <summary>Vencimiento del lote del kit: el más cercano de los lotes de sus piezas; null si ninguno vence.</summary>
    public static DateOnly? KitExpiry(IEnumerable<DateOnly?> pieceExpiries)
    {
        DateOnly? min = null;
        foreach (var e in pieceExpiries)
            if (e is DateOnly d && (min is null || d < min)) min = d;
        return min;
    }

    /// <summary>Kits completos que cubre una cantidad disponible (hacia abajo; 0 si no hay o la cantidad por kit no es positiva).</summary>
    public static int KitsCoveredBy(decimal available, decimal qtyPerKit)
        => qtyPerKit <= 0m || available <= 0m ? 0 : (int)Math.Min(int.MaxValue, Math.Floor(available / qtyPerKit));

    /// <summary>
    /// Cuántos kits se pueden armar EN UN SOLO ARMADO en un almacén: por pieza, lo que cubre su mejor lote (un solo lote por pieza) o, sin lote, todo lo disponible;
    /// el menor entre las piezas. <paramref name="pieces"/> = (cantidad por kit, disponible por lote; LotId null = sin lote).
    /// </summary>
    public static int CanAssembleInOne(IEnumerable<(decimal QtyPerKit, bool LotTracked, IReadOnlyDictionary<int, decimal> ByLot, decimal Total)> pieces)
    {
        var list = pieces.ToList();
        if (list.Count == 0) return 0;
        return list.Min(p => p.LotTracked
            ? (p.ByLot.Count == 0 ? 0 : p.ByLot.Values.Max(a => KitsCoveredBy(a, p.QtyPerKit)))
            : KitsCoveredBy(p.Total, p.QtyPerKit));
    }

    /// <summary>
    /// Reparte la devolución de una pieza entre las líneas del armado (en su orden), sin pasar lo que cada línea sacó menos lo que ya volvió. Devuelve (índice de línea,
    /// cantidad); si lo pedido supera lo pendiente, el resto queda sin repartir (el llamador lo valida antes).
    /// </summary>
    public static IReadOnlyList<(int Index, decimal Quantity)> SplitReturn(decimal quantity, IReadOnlyList<(decimal Taken, decimal Returned)> lines)
    {
        var result = new List<(int, decimal)>();
        var left = quantity;
        for (var i = 0; i < lines.Count && left > 0m; i++)
        {
            var room = lines[i].Taken - lines[i].Returned;
            if (room <= 0m) continue;
            var take = Math.Min(room, left);
            result.Add((i, take));
            left -= take;
        }
        return result;
    }
}
