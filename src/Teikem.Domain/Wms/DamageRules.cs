using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// 2026-10-08 — reglas puras de los reportes de daño (sin base de datos). Los mensajes los citan el servicio, las pruebas, el manual y la FAQ.
/// </summary>
public static class DamageRules
{
    public const string BodyRequired = "El cuerpo de la solicitud es obligatorio.";
    public const string ProductRequired = "Indique el producto dañado.";
    public const string QuantityInvalid = "La cantidad dañada debe ser mayor que 0.";
    public const string ReceiptRequired = "Indique el recibo del que llegó dañado.";
    public const string FromBinRequired = "Indique la posición donde está lo dañado.";
    public const string CauseRequired = "Indique cómo ocurrió el daño.";
    public const string DispositionRequired = "Indique si lo dañado va a cuarentena o se desecha.";
    public const string NoQuarantineBin = "El almacén no tiene una posición de cuarentena activa; cree una en una zona de tipo Cuarentena.";
    public const string SerialNotSupported = "Los productos con serie todavía no se reportan aquí; use un ajuste de inventario con motivo Daño.";
    public const string ResolveOnlyQuarantined = "Solo se resuelve un daño que está en cuarentena.";
    public const string RecoverBinRequired = "Indique la posición a la que vuelve lo recuperado.";
    public const string FinalDestinationRequired = "Indique a dónde va lo desechado (tirado, devuelto al proveedor, donado…).";
    public static string UnknownFinalDestination(string value) => $"Destino final desconocido: '{value}'.";

    /// <summary>Destino final normalizado (mayúsculas) o error. En un catálogo editable no se restringe a los valores de fábrica: lo valida el servicio contra LookupCode.</summary>
    public static (string? Value, string? Error) ParseFinalDestination(string? value)
        => string.IsNullOrWhiteSpace(value) ? (null, FinalDestinationRequired) : (value.Trim().ToUpperInvariant(), null);

    /// <summary>¿Hay que reservar lo dañado en su posición? Solo si la posición NO está ya excluida de la asignación (cuarentena, cruce de muelle, renta).</summary>
    public static bool NeedsReservation(string? zoneTypeCode) => !StockAllocator.IsExcludedZone(zoneTypeCode);

    public const string NotesTooLong = "La nota no puede pasar de 300 caracteres.";
    public const int NotesMax = 300;

    public static string UnknownOrigin(string value) => $"Origen desconocido: '{value}'. Use RECEIPT o WAREHOUSE.";
    public static string UnknownCause(string value) => $"Causa desconocida: '{value}'.";
    public static string UnknownDisposition(string value) => $"Destino desconocido: '{value}'. Use QUARANTINE o DISCARD.";
    public static string BinInactive(string code) => $"La posición {code} está desactivada.";
    public static string RecoverZoneNotAllowed(string code, string zoneType) => $"La posición {code} está en una zona {zoneType}; lo recuperado vuelve a una posición de guardado.";

    /// <summary>Código visible DAN-#####.</summary>
    public static string Code(int id) => $"DAN-{id:00000}";

    /// <summary>Origen normalizado (mayúsculas) o mensaje de error.</summary>
    public static (string? Value, string? Error) ParseOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var v = value.Trim().ToUpperInvariant();
        return DamageOrigins.All.Contains(v) ? (v, null) : (null, UnknownOrigin(value.Trim()));
    }

    public static (string? Value, string? Error) ParseDisposition(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, DispositionRequired);
        var v = value.Trim().ToUpperInvariant();
        return DamageDispositions.All.Contains(v) ? (v, null) : (null, UnknownDisposition(value.Trim()));
    }

    public static (string? Value, string? Error) ParseCause(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, CauseRequired);
        var v = value.Trim().ToUpperInvariant();
        return DamageCauses.All.Contains(v) ? (v, null) : (null, UnknownCause(value.Trim()));
    }

    /// <summary>Nota recortada (null si queda vacía) o error por largo.</summary>
    public static (string? Value, string? Error) NormalizeNotes(string? notes)
    {
        var t = notes?.Trim();
        if (string.IsNullOrEmpty(t)) return (null, null);
        return t.Length > NotesMax ? (null, NotesTooLong) : (t, null);
    }

    /// <summary>
    /// Un recibo no tiene "posición de origen": lo dañado nunca entró como bueno. Un daño del almacén sí necesita saber dónde estaba. Devuelve el
    /// error por campo o null.
    /// </summary>
    public static (string Field, string Message)? ValidateSource(string origin, bool hasReceipt, bool hasFromBin)
    {
        if (origin == DamageOrigins.Receipt && !hasReceipt) return ("receiptPublicId", ReceiptRequired);
        if (origin == DamageOrigins.Warehouse && !hasFromBin) return ("fromBinId", FromBinRequired);
        return null;
    }

    /// <summary>¿Se mueve inventario? Un daño de recibo que se desecha de una vez nunca entró al inventario: no hay movimiento.</summary>
    public static bool MovesInventory(string origin, string disposition)
        => !(origin == DamageOrigins.Receipt && disposition == DamageDispositions.Discard);

    /// <summary>La zona de la posición a la que vuelve lo recuperado: de guardado (nunca cuarentena, recepción, cruce de muelle ni renta).</summary>
    public static string? ValidateRecoverZone(string binCode, string? zoneTypeCode)
        => zoneTypeCode is ZoneTypes.Quarantine or ZoneTypes.Staging or ZoneTypes.CrossDock or ZoneTypes.Rental
            ? RecoverZoneNotAllowed(binCode, zoneTypeCode)
            : null;

    // ---- daño declarado en la línea del recibo (2026-10-08)
    public const string LineDamagedTooMuch = "La cantidad dañada no puede ser mayor que la cantidad recibida.";
    public const string LineDamagedDecimals = "La cantidad dañada admite hasta 3 decimales.";
    public const string LineOtherNeedsNote = "Escriba la razón del daño cuando la causa es Otro.";
    public const string LineDamageWithoutQty = "Indique la cantidad dañada.";

    /// <summary>
    /// Valida el daño declarado en una línea de recibo y devuelve (campo, mensaje) por cada error. Sin cantidad dañada (null o 0) no hay daño: lo demás
    /// se ignora. Con ella: no ser mayor que lo recibido ni pasar de 3 decimales, producto sin serie, causa del catálogo, y si la causa es Otro, el comentario.
    /// </summary>
    public static IReadOnlyList<(string Field, string Message)> ValidateLineDamage(decimal? damaged, decimal received, bool serialProduct, string? cause, string? note)
    {
        var errors = new List<(string, string)>();
        if (damaged is null or 0m) return errors;
        if (damaged < 0m) { errors.Add(("damagedQty", QuantityInvalid)); return errors; }
        if (decimal.Round(damaged.Value, 3) != damaged.Value) errors.Add(("damagedQty", LineDamagedDecimals));
        else if (damaged > received) errors.Add(("damagedQty", LineDamagedTooMuch));
        if (serialProduct) errors.Add(("damagedQty", SerialNotSupported));
        var (causeCode, causeError) = ParseCause(cause);
        if (causeError is not null) errors.Add(("damageCause", causeError));
        else if (causeCode == DamageCauses.Other && string.IsNullOrWhiteSpace(note)) errors.Add(("damageNote", LineOtherNeedsNote));
        var (_, noteError) = NormalizeNotes(note);
        if (noteError is not null) errors.Add(("damageNote", noteError));
        return errors;
    }

    /// <summary>Nota de los movimientos del ledger: 'DAN-00012 · Llegó dañado · {nota}'.</summary>
    public static string MovementNote(int id, string causeLabel, string? notes)
        => string.IsNullOrWhiteSpace(notes) ? $"{Code(id)} · {causeLabel}" : $"{Code(id)} · {causeLabel} · {notes.Trim()}";
}
