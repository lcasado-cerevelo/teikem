using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 16 — recibo directo a posición (puro). El modo de recepción vive en el almacén (por defecto de los recibos nuevos) y
/// cada recibo guarda la copia con que se abrió (D1, D2); NULL = PUTAWAY. En modo DIRECT cada línea con mercancía lleva su
/// posición destino (de guardado: nunca STAGING ni CROSSDOCK, D5; la cuarentena sí) y al confirmar entra ahí sin tareas; el
/// cupo solo avisa (D4). Una línea con cruce de muelle asignado entra a la posición de recepción como siempre (D11).
/// Mensajes públicos: los usan el servicio, las pruebas y el manual (FAQ del Lote 16).
/// </summary>
public static class ReceivingModeRules
{
    public static string UnknownMode(string value) => $"Modo de recepción desconocido: '{value}'. Use PUTAWAY o DIRECT.";
    public static string TargetRequired(string sku) => $"Indique la posición destino de {sku}: el recibo entra directo a posición.";
    public static string TargetZoneNotAllowed(string binCode, string zoneType)
        => $"La posición {binCode} está en una zona {zoneType}; la posición destino debe ser de guardado.";
    public static string TargetCodeNotFound(string binCode) => $"La posición {binCode} no existe en el almacén del recibo.";
    public const string TargetIdAndCode = "Indique la posición destino por id o por código, no ambos.";
    public static string SameProductOtherTarget(string sku, string binCode)
        => $"Ya se capturó {sku} con destino {binCode}; en un recibo con aviso u orden de compra cada línea entra a una sola posición.";
    public static string TargetBinInactive(string binCode) => $"La posición destino {binCode} está desactivada.";
    public static string TargetZoneInactive(string binCode) => $"La zona de la posición destino {binCode} está inactiva.";

    /// <summary>Máximo de sugerencias de posición destino por línea (GET target-suggestions?take=).</summary>
    public const int MaxSuggestions = 10;
    public const int DefaultSuggestions = 5;

    /// <summary>null o vacío = sin valor (Mode null, sin error); PUTAWAY/DIRECT sin distinguir mayúsculas; otro → mensaje del 400.</summary>
    public static (string? Mode, string? Error) ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var v = value.Trim().ToUpperInvariant();
        return ReceivingModes.All.Contains(v) ? (v, null) : (null, UnknownMode(value.Trim()));
    }

    /// <summary>Código normalizado; null o desconocido = PUTAWAY.</summary>
    public static string Normalize(string? mode)
        => string.Equals(mode?.Trim(), ReceivingModes.Direct, StringComparison.OrdinalIgnoreCase) ? ReceivingModes.Direct : ReceivingModes.Putaway;

    public static bool IsDirect(string? mode) => Normalize(mode) == ReceivingModes.Direct;

    /// <summary>Modo efectivo de un recibo: su copia; sin copia, el del almacén; sin ninguno, PUTAWAY.</summary>
    public static string Effective(string? receiptMode, string? warehouseMode)
        => Normalize(string.IsNullOrWhiteSpace(receiptMode) ? warehouseMode : receiptMode);

    /// <summary>
    /// Modo con que nace un recibo: el pedido si viene; si no, el del almacén. D9 (app vieja): un alta atómica (confirm =
    /// true) sin modo y sin ninguna posición destino en un almacén directo entra "Con acomodo" (con tareas), no se pierde.
    /// </summary>
    public static string CreateMode(string? requested, string? warehouseMode, bool confirm, bool anyTarget)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return Normalize(requested);
        var warehouse = Normalize(warehouseMode);
        if (warehouse == ReceivingModes.Direct && confirm && !anyTarget) return ReceivingModes.Putaway;
        return warehouse;
    }

    /// <summary>
    /// ¿La línea necesita posición destino para confirmar un recibo directo? Sí si recibió algo (&gt; 0), salvo un producto
    /// por lote sin lote (no mueve inventario).
    /// </summary>
    public static bool NeedsTarget(string? trackingCode, decimal received, bool hasLot)
    {
        if (received <= 0m) return false;
        if (string.Equals(trackingCode, TrackingTypes.Lot, StringComparison.OrdinalIgnoreCase) && !hasLot) return false;
        return true;
    }

    /// <summary>
    /// D5: la posición destino no puede ser de recepción (STAGING) ni de cruce de muelle (CROSSDOCK); Lote 27: ni de la zona En renta
    /// (RENTAL). null = permitida.
    /// </summary>
    public static string? ValidateTargetZone(string binCode, string? zoneTypeCode)
        => zoneTypeCode is ZoneTypes.Staging or ZoneTypes.CrossDock or ZoneTypes.Rental ? TargetZoneNotAllowed(binCode, zoneTypeCode) : null;

    /// <summary>
    /// Espacio libre de la posición en unidades: cupo − existencia − lo reservado por otras líneas (nunca negativo); null si
    /// la posición no tiene cupo configurado.
    /// </summary>
    public static decimal? FreeQty(int? maxCapacityQty, decimal onHand, decimal claimed)
        => maxCapacityQty is int max ? Math.Max(0m, max - onHand - claimed) : null;

    /// <summary>¿La cantidad excede el espacio libre? Sin cupo nunca excede (D4: solo avisa, no bloquea).</summary>
    public static bool Exceeds(decimal? free, decimal quantity) => free is decimal f && quantity > f;

    /// <summary>
    /// Posición donde se asienta la línea al confirmar: en DIRECT, su destino, salvo con cruce de muelle asignado (entra a
    /// la de recepción, D11); en PUTAWAY, la de recepción.
    /// </summary>
    public static int? PostingBin(string mode, bool hasCrossDock, int? targetBinId, int? stagingBinId)
        => IsDirect(mode) && !hasCrossDock ? targetBinId : stagingBinId;
}
