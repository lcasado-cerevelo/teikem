using System.Globalization;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>Dirección y magnitud de un ajuste manual: entrada (+, a To) o salida (−, desde From). Error si no procede.</summary>
public sealed record AdjustmentPosting(bool IsEntry, decimal Magnitude, string? Error = null)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// Lote 6 (P3) — reglas puras del ajuste manual de inventario (± con motivo de catálogo, D7) y de la transferencia entre
/// posiciones o almacenes (una sola fila TRANSFER, D40). Sin BD.
/// - El usuario captura la cantidad CON signo: &gt; 0 entra a la posición (To), &lt; 0 sale de ella (From), 0 → 400.
///   El ledger recibe la MAGNITUD y guarda el signo (D3); los decimales y el máximo los valida el ledger.
/// - El motivo es obligatorio y sale del catálogo AdjustmentReason. RECEIPT_VARIANCE, COUNT_VARIANCE y PICK_BATCH_REVERSAL
///   los asigna el sistema (recepción, conteo, eliminación de recolección): un usuario no los puede usar.
/// - Seguimiento: LOT exige lote; NONE no admite lote; SERIAL admite lote opcional y exige tantas series como la cantidad
///   (entera). Un producto sin serie no admite series.
/// Los mensajes son públicos porque el manual y la FAQ los citan.
/// </summary>
public static class AdjustmentRules
{
    public const int MaxSerialLength = 80;
    public const int MaxSerialsPerLine = 500;
    public const int MaxNotesLength = 300;
    public const int MaxLotNumberLength = 60;

    public const string QuantityRequired = "Indique la cantidad del ajuste.";
    public const string ZeroQuantity = "La cantidad del ajuste no puede ser cero.";
    public const string ReasonRequired = "Indique el motivo del ajuste.";
    public const string BinRequired = "Indique la posición.";
    public const string ProductRequired = "Indique el producto.";
    public const string TransferQuantityRequired = "Indique la cantidad a transferir.";
    public const string SameBin = "La posición de origen y la de destino son la misma.";
    public const string FromBinRequired = "Indique la posición de origen.";
    public const string ToBinRequired = "Indique la posición de destino.";
    public const string SerialInteger = "En productos con serie la cantidad debe ser entera.";
    public const string SerialsTooMany = "Una línea admite como máximo 500 números de serie.";
    public const string NotesTooLong = "Las notas admiten como máximo 300 caracteres.";
    public const string LotNumberRequired = "Indique el número de lote.";
    public const string LotNumberTooLong = "El número de lote admite como máximo 60 caracteres.";
    public const string LotDates = "La fecha de fabricación no puede ser posterior al vencimiento.";
    public const string LotAmbiguous = "Indique el lote por su id o por su número, no ambos.";
    public const string LotExitNeedsExisting = "Para sacar inventario indique un lote existente.";

    /// <summary>Motivos que solo asigna el sistema (400 SystemReason si un usuario los envía).</summary>
    public static readonly IReadOnlySet<string> SystemReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        AdjustmentReasons.ReceiptVariance, AdjustmentReasons.CountVariance, AdjustmentReasons.PickBatchReversal,
    };

    public static string SystemReason(string code) => $"El motivo {code} lo asigna el sistema.";
    public static string UnknownReason(string code) => $"Motivo de ajuste desconocido: '{code}'.";
    public static string LotRequired(string sku) => $"El producto {sku} se controla por lote; indique el lote.";
    public static string LotNotAllowed(string sku) => $"El producto {sku} no se controla por lote; no indique lote.";
    public static string SerialsRequired(string sku) => $"El producto {sku} se controla por serie; capture los números de serie.";
    public static string SerialNotAllowed(string sku) => $"El producto {sku} no se controla por serie; no capture números de serie.";
    public static string SerialCountMismatch(int count, decimal quantity)
        => $"Se capturaron {count} serie(s) para una cantidad de {Format(quantity)}; deben coincidir.";
    public static string SerialTooLong(string serial) => $"El número de serie {serial} excede {MaxSerialLength} caracteres.";
    public static string SerialDuplicated(string serial) => $"El número de serie {serial} está repetido.";
    public static string LotExistsWithOtherDates(string lotNumber)
        => $"El lote {lotNumber} ya existe con otras fechas; corrija las fechas o use otro número de lote.";

    /// <summary>
    /// Cantidad con signo → dirección y magnitud: &gt; 0 entrada a To; &lt; 0 salida desde From; 0 → ZeroQuantity;
    /// NULL → QuantityRequired.
    /// </summary>
    public static AdjustmentPosting ToPosting(decimal? quantity)
    {
        if (quantity is null) return new AdjustmentPosting(false, 0m, QuantityRequired);
        if (quantity.Value == 0m) return new AdjustmentPosting(false, 0m, ZeroQuantity);
        return quantity.Value > 0m ? new AdjustmentPosting(true, quantity.Value) : new AdjustmentPosting(false, -quantity.Value);
    }

    /// <summary>
    /// Motivo del ajuste: obligatorio (ReasonRequired), del catálogo (UnknownReason) y no reservado al sistema (SystemReason).
    /// Devuelve el código normalizado o el error.
    /// </summary>
    public static (string? Code, string? Error) ValidateReason(string? reason, IEnumerable<string> catalogCodes)
    {
        if (string.IsNullOrWhiteSpace(reason)) return (null, ReasonRequired);
        var code = reason.Trim().ToUpperInvariant();
        if (SystemReasons.Contains(code)) return (null, SystemReason(code));
        if (!catalogCodes.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase))) return (null, UnknownReason(code));
        return (code, null);
    }

    /// <summary>
    /// Normaliza los números de serie: recorta, descarta vacíos, máximo 80 caracteres, sin duplicados (sin distinguir
    /// mayúsculas) y como máximo 500 por línea. NULL → lista vacía.
    /// </summary>
    public static (IReadOnlyList<string> Serials, string? Error) NormalizeSerials(IEnumerable<string?>? serials)
    {
        var result = new List<string>();
        if (serials is null) return (result, null);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in serials)
        {
            var s = raw?.Trim();
            if (string.IsNullOrEmpty(s)) continue;
            if (s.Length > MaxSerialLength) return (Array.Empty<string>(), SerialTooLong(s));
            if (!seen.Add(s)) return (Array.Empty<string>(), SerialDuplicated(s));
            result.Add(s);
        }
        if (result.Count > MaxSerialsPerLine) return (Array.Empty<string>(), SerialsTooMany);
        return (result, null);
    }

    /// <summary>
    /// Seguimiento del producto contra lo capturado: LOT exige lote; NONE no admite lote; SERIAL exige una serie por
    /// unidad (cantidad entera igual al número de series); los demás no admiten series. Devuelve (campo, mensaje) o NULL.
    /// </summary>
    public static (string Field, string Message)? ValidateTracking(string trackingCode, string sku, decimal magnitude, bool hasLot, int serialCount)
    {
        var tracking = (trackingCode ?? TrackingTypes.None).ToUpperInvariant();
        if (tracking == TrackingTypes.Lot && !hasLot) return ("lot", LotRequired(sku));
        if (tracking == TrackingTypes.None && hasLot) return ("lot", LotNotAllowed(sku));
        if (tracking == TrackingTypes.Serial)
        {
            if (magnitude != decimal.Truncate(magnitude)) return ("quantity", SerialInteger);
            if (serialCount == 0) return ("serialNumbers", SerialsRequired(sku));
            if (serialCount != magnitude) return ("serialNumbers", SerialCountMismatch(serialCount, magnitude));
            return null;
        }
        if (serialCount > 0) return ("serialNumbers", SerialNotAllowed(sku));
        return null;
    }

    /// <summary>Datos de un lote nuevo o por reutilizar: número obligatorio (≤ 60) y fabricación ≤ vencimiento.</summary>
    public static (string? Number, string? Error) ValidateLotInput(string? number, DateOnly? manufacture, DateOnly? expiry)
    {
        var n = number?.Trim();
        if (string.IsNullOrEmpty(n)) return (null, LotNumberRequired);
        if (n.Length > MaxLotNumberLength) return (null, LotNumberTooLong);
        if (manufacture is DateOnly m && expiry is DateOnly e && m > e) return (null, LotDates);
        return (n, null);
    }

    /// <summary>
    /// Un lote existente con el mismo número se reutiliza solo si las fechas capturadas coinciden (una fecha no capturada
    /// no se compara). Fechas distintas → 409 LotExistsWithOtherDates (D34).
    /// </summary>
    public static bool LotDatesMatch(DateOnly? existingManufacture, DateOnly? existingExpiry, DateOnly? manufacture, DateOnly? expiry)
        => (manufacture is null || manufacture == existingManufacture) && (expiry is null || expiry == existingExpiry);

    /// <summary>Transferencia: origen y destino obligatorios y distintos.</summary>
    public static (string Field, string Message)? ValidateTransferBins(int? fromBinId, int? toBinId)
    {
        if (fromBinId is null) return ("fromBinId", FromBinRequired);
        if (toBinId is null) return ("toBinId", ToBinRequired);
        if (fromBinId == toBinId) return ("toBinId", SameBin);
        return null;
    }

    /// <summary>Notas recortadas (NULL si vacías) o NotesTooLong.</summary>
    public static (string? Notes, string? Error) NormalizeNotes(string? notes)
    {
        var n = notes?.Trim();
        if (string.IsNullOrEmpty(n)) return (null, null);
        return n.Length > MaxNotesLength ? (null, NotesTooLong) : (n, null);
    }

    private static string Format(decimal q) => q.ToString("0.###", CultureInfo.InvariantCulture);
}
