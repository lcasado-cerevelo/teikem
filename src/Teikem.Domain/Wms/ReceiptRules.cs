using System.Globalization;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>Origen legible de un recibo (columna 'Origen' de la lista y de la fuente RECEIPT).</summary>
public static class ReceiptOrigins
{
    /// <summary>Recibo contra orden de compra (su ASN nació de la PO).</summary>
    public const string PurchaseOrder = "PO";
    /// <summary>Recibo contra el aviso de llegada de un cliente 3PL.</summary>
    public const string Asn = "ASN";
    public const string Blind = "BLIND";
    public const string Return = "RETURN";
}

/// <summary>
/// Lote 6 (P4) — reglas puras de la Recepción (R7, R8, R9, R18). Sin BD: el servicio las aplica y el manual/FAQ citan los
/// mensajes exactos de aquí (public const o métodos estáticos, cultura invariante).
/// - Un recibo 'ASN' (contra un aviso de cliente o contra una PO) espera cantidades: sus líneas del aviso llevan
///   ExpectedQty y las líneas extra (sin línea del aviso) entran como ajuste. Los recibos ciegos y de devolución no
///   esperan nada: lo recibido es el RECEIPT completo (D4).
/// - La cantidad recibida arranca igual a la esperada (R8); el receptor la corrige antes de confirmar.
/// - Cantidades en DECIMAL(16,3): 0 se admite como recibido (llegó nada), negativos no; enteras para SERIAL.
/// </summary>
public static class ReceiptRules
{
    public const int MaxLines = 200;
    public const int MaxSerialsPerLine = 500;
    public const int SerialMaxLength = 80;
    public const int LotNumberMaxLength = 60;
    public const int ReferenceMaxLength = 80;
    public const int MaxPageSize = 200;
    /// <summary>Máximo de DECIMAL(16,3).</summary>
    public const decimal MaxQuantity = 9_999_999_999_999.999m;

    // ---------------------------------------------------------------- mensajes del plan

    public static string ReceiptNotOpen(string number) => $"El recibo {number} ya fue confirmado; no se puede modificar.";
    public const string AsnBusy = "El aviso de llegada ya tiene un recibo abierto o confirmado.";
    public const string AsnNotExpected = "El aviso de llegada no está pendiente de recibir.";
    public const string NoStagingBin = "El almacén no tiene una posición de recepción (zona STAGING); indíquela.";
    public const string StagingMustBeStaging = "La posición de recepción debe estar en una zona STAGING o CROSSDOCK.";
    public static string OwnerMismatch(string sku) => $"El producto {sku} no pertenece al cliente del aviso de llegada.";
    public static string OwnProductsOnly(string sku) => $"La orden de compra solo admite productos propios; {sku} pertenece a un cliente.";
    public const string AsnLineNotRemovable = "Las líneas del aviso de llegada no se eliminan; capture 0 como recibido.";
    public const string LineHasCrossDock = "La línea tiene asignaciones de cruce de muelle; cancélelas antes de eliminarla.";

    // ---------------------------------------------------------------- mensajes propios de la pieza

    public const string ReceiptHasCrossDock = "El recibo tiene asignaciones de cruce de muelle; cancélelas antes de eliminarlo.";
    public const string TooManyLines = "El recibo admite como máximo 200 líneas.";
    public const string AsnTooManyLines = "El aviso de llegada admite como máximo 200 líneas.";
    public const string LinesRequired = "Indique al menos una línea.";
    public const string NoLinesToConfirm = "El recibo no tiene líneas; agregue al menos una antes de confirmar.";
    public const string AsnOrPurchaseOrder = "Indique el aviso de llegada o la orden de compra, no ambos.";
    public const string AsnOtherWarehouse = "El aviso de llegada es de otro almacén.";
    public const string PurchaseOrderOtherWarehouse = "La orden de compra es de otro almacén.";
    public const string PurchaseOrderHasOpenReceipt = "La orden de compra ya tiene un recibo abierto; confírmelo o elimínelo antes de recibir de nuevo.";
    public const string AsnTypeNeedsDocument = "Un recibo con aviso de llegada se crea indicando el aviso (asnId) o la orden de compra (purchaseOrderPublicId).";
    public static string UnknownType(string type) => $"Tipo de recepción desconocido: '{type}'. Use ASN, BLIND o RETURN.";
    public const string WarehouseInactive = "El almacén está dado de baja; no admite recepciones.";
    public const string StagingBinInactive = "La posición de recepción está desactivada.";
    public static string ProductInactive(string sku) => $"El producto {sku} está dado de baja; no se puede recibir.";
    public const string ClientRequired = "Indique el cliente dueño de la mercancía del aviso de llegada.";
    public const string AsnHasOpenReceipt = "El aviso de llegada tiene un recibo abierto; elimínelo antes de cancelar.";
    public const string ReferenceTooLong = "La referencia admite como máximo 80 caracteres.";
    public const string LineNotFoundWhat = "Línea del recibo";

    // cantidades
    public const string ReceivedQtyRequired = "Indique la cantidad recibida.";
    public const string ReceivedQtyNegative = "La cantidad recibida no puede ser negativa.";
    public const string ExpectedQtyPositive = "La cantidad esperada debe ser mayor que cero.";
    public const string QtyDecimals = "La cantidad admite como máximo 3 decimales.";
    public const string QtyTooLarge = "La cantidad excede el máximo permitido.";

    // seguimiento
    public static string LotRequired(string sku) => $"El producto {sku} se controla por lote: indique el lote.";
    public static string LotNotAllowed(string sku) => $"El producto {sku} no se controla por lote; no indique lote.";
    public static string SerialsNotAllowed(string sku) => $"El producto {sku} no se controla por serie; no capture números de serie.";
    public static string SerialIntegerQty(string sku) => $"El producto {sku} se controla por serie: la cantidad debe ser entera.";
    public static string SerialCountMismatch(string sku, decimal quantity, int serials)
        => $"El producto {sku} se controla por serie: capture {FormatQty(quantity)} número(s) de serie (hay {serials.ToString(CultureInfo.InvariantCulture)}).";

    // series y lotes
    public const string SerialEmpty = "Los números de serie no pueden estar vacíos.";
    public const string SerialTooLong = "Cada número de serie admite como máximo 80 caracteres.";
    public static string SerialDuplicated(string serial) => $"El número de serie '{serial}' está repetido.";
    public const string TooManySerials = "Una línea admite como máximo 500 números de serie.";
    public const string LotNumberRequired = "Indique el número de lote.";
    public const string LotNumberTooLong = "El número de lote admite como máximo 60 caracteres.";
    public const string LotDates = "La fecha de vencimiento no puede ser anterior a la de fabricación.";
    public static string LotExistsWithOtherDates(string lotNumber)
        => $"El lote {lotNumber} ya existe con otras fechas; corrija las fechas o use otro número de lote.";
    public static string LotCreatedConcurrently(string lotNumber)
        => $"El lote {lotNumber} se acaba de crear en otra operación; intente de nuevo.";

    /// <summary>
    /// ¿Las fechas capturadas coinciden con las del lote existente? Una fecha no capturada (null) no se compara; una
    /// capturada debe ser igual a la guardada (si la guardada es null, es 'otra fecha': las fechas no se sobrescriben, D34).
    /// </summary>
    public static bool LotDatesMatch(DateOnly? existingManufacture, DateOnly? existingExpiry, DateOnly? capturedManufacture, DateOnly? capturedExpiry)
        => (capturedManufacture is null || capturedManufacture == existingManufacture)
           && (capturedExpiry is null || capturedExpiry == existingExpiry);

    // ---------------------------------------------------------------- tipo y origen

    /// <summary>Tipo de recibo (ReceiptType) según el origen: PO y ASN → ASN; ciego → BLIND; devolución → RETURN.</summary>
    public static string TypeFor(string origin) => origin switch
    {
        ReceiptOrigins.PurchaseOrder or ReceiptOrigins.Asn => ReceiptTypes.Asn,
        ReceiptOrigins.Blind => ReceiptTypes.Blind,
        ReceiptOrigins.Return => ReceiptTypes.Return,
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Origen de recibo desconocido."),
    };

    /// <summary>Origen legible: PO si el aviso nació de una orden de compra, ASN si es de cliente; si no, el tipo.</summary>
    public static string OriginOf(string typeCode, bool hasAsn, bool asnFromPurchaseOrder)
    {
        if (hasAsn) return asnFromPurchaseOrder ? ReceiptOrigins.PurchaseOrder : ReceiptOrigins.Asn;
        return typeCode == ReceiptTypes.Return ? ReceiptOrigins.Return : ReceiptOrigins.Blind;
    }

    /// <summary>
    /// Tipo pedido por el cliente del API para un recibo SIN aviso ni PO: vacío → BLIND; BLIND/RETURN tal cual; ASN → 400
    /// (un recibo ASN nace del aviso o de la PO); otro → 400. Devuelve (código, error).
    /// </summary>
    public static (string? Type, string? Error) ParseManualType(string? type)
    {
        var t = type?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(t)) return (ReceiptTypes.Blind, null);
        return t switch
        {
            ReceiptTypes.Blind or ReceiptTypes.Return => (t, null),
            ReceiptTypes.Asn => (null, AsnTypeNeedsDocument),
            _ => (null, UnknownType(type!.Trim())),
        };
    }

    /// <summary>¿El recibo espera cantidades? Solo el tipo ASN (aviso de cliente o PO).</summary>
    public static bool ExpectsQuantities(string typeCode) => typeCode == ReceiptTypes.Asn;

    // ---------------------------------------------------------------- cantidades

    /// <summary>R8: al abrir el recibo, lo recibido arranca igual a lo esperado.</summary>
    public static decimal InitialReceived(decimal expected) => expected;

    /// <summary>Lo esperado de una línea para la varianza: el del aviso; una línea extra de un recibo ASN espera 0; un recibo ciego espera lo recibido.</summary>
    public static decimal ExpectedFor(bool expectsQuantities, decimal? expected, decimal received)
        => expected ?? (expectsQuantities ? 0m : received);

    /// <summary>Varianza de la línea = recibido − esperado (0 en ciegos y devoluciones).</summary>
    public static decimal LineVariance(bool expectsQuantities, decimal? expected, decimal received)
        => received - ExpectedFor(expectsQuantities, expected, received);

    public static bool HasVariance(bool expectsQuantities, decimal? expected, decimal received)
        => LineVariance(expectsQuantities, expected, received) != 0m;

    /// <summary>Cantidad recibida: obligatoria, ≥ 0, a lo más 3 decimales y dentro de DECIMAL(16,3). Devuelve el error o null.</summary>
    public static string? ValidateReceivedQty(decimal? qty)
    {
        if (qty is null) return ReceivedQtyRequired;
        if (qty.Value < 0m) return ReceivedQtyNegative;
        return ValidateScale(qty.Value);
    }

    /// <summary>Cantidad esperada de un aviso de llegada: &gt; 0, a lo más 3 decimales.</summary>
    public static string? ValidateExpectedQty(decimal? qty)
    {
        if (qty is null || qty.Value <= 0m) return ExpectedQtyPositive;
        return ValidateScale(qty.Value);
    }

    private static string? ValidateScale(decimal qty)
    {
        if (qty > MaxQuantity) return QtyTooLarge;
        if (decimal.Round(qty, 3) != qty) return QtyDecimals;
        return null;
    }

    public static bool IsInteger(decimal qty) => decimal.Truncate(qty) == qty;

    // ---------------------------------------------------------------- seguimiento

    /// <summary>
    /// Seguimiento de una línea al confirmar (o al capturar): NONE sin lote ni series; LOT con lote cuando se recibió algo
    /// (una línea LOT en 0 sin lote no mueve inventario) y sin series; SERIAL con cantidad entera igual al número de series
    /// (el lote es opcional). Devuelve el mensaje o null.
    /// </summary>
    public static string? ValidateTracking(string trackingCode, string sku, decimal received, bool hasLot, int serialCount)
    {
        switch (trackingCode)
        {
            case TrackingTypes.Lot:
                if (serialCount > 0) return SerialsNotAllowed(sku);
                if (!hasLot && received > 0m) return LotRequired(sku);
                return null;
            case TrackingTypes.Serial:
                if (!IsInteger(received)) return SerialIntegerQty(sku);
                if (serialCount != received) return SerialCountMismatch(sku, received, serialCount);
                return null;
            default:
                if (hasLot) return LotNotAllowed(sku);
                if (serialCount > 0) return SerialsNotAllowed(sku);
                return null;
        }
    }

    /// <summary>
    /// Captura (sin exigir aún la cuadratura de series): NONE no admite lote ni series; LOT no admite series; SERIAL exige
    /// cantidad entera. La cuadratura series = cantidad se exige al confirmar.
    /// </summary>
    public static string? ValidateCapture(string trackingCode, string sku, decimal received, bool hasLot, int serialCount)
    {
        switch (trackingCode)
        {
            case TrackingTypes.Lot:
                return serialCount > 0 ? SerialsNotAllowed(sku) : null;
            case TrackingTypes.Serial:
                return IsInteger(received) ? null : SerialIntegerQty(sku);
            default:
                if (hasLot) return LotNotAllowed(sku);
                return serialCount > 0 ? SerialsNotAllowed(sku) : null;
        }
    }

    /// <summary>
    /// Normaliza los números de serie de una línea: recorta, descarta nada (una serie vacía es error), máximo 80
    /// caracteres cada una, sin duplicados (sin distinguir mayúsculas) y máximo 500 por línea. Conserva el orden.
    /// </summary>
    public static (IReadOnlyList<string> Serials, string? Error) NormalizeSerials(IEnumerable<string?>? serials)
    {
        var list = new List<string>();
        if (serials is null) return (list, null);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in serials)
        {
            var s = raw?.Trim();
            if (string.IsNullOrEmpty(s)) return (list, SerialEmpty);
            if (s.Length > SerialMaxLength) return (list, SerialTooLong);
            if (!seen.Add(s)) return (list, SerialDuplicated(s));
            list.Add(s);
            if (list.Count > MaxSerialsPerLine) return (list, TooManySerials);
        }
        return (list, null);
    }

    /// <summary>Número de lote: obligatorio, recortado, máximo 60; vencimiento no anterior a la fabricación.</summary>
    public static (string? Number, string? Error) NormalizeLot(string? number, DateOnly? manufactureDate, DateOnly? expiryDate)
    {
        var n = number?.Trim();
        if (string.IsNullOrEmpty(n)) return (null, LotNumberRequired);
        if (n.Length > LotNumberMaxLength) return (null, LotNumberTooLong);
        if (manufactureDate is DateOnly m && expiryDate is DateOnly e && e < m) return (null, LotDates);
        return (n, null);
    }

    public static string FormatQty(decimal qty) => qty.ToString("0.###", CultureInfo.InvariantCulture);
}
