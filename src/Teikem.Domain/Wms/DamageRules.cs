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
    public const string NotesTooLong = "La nota no puede pasar de 300 caracteres.";
    public const int NotesMax = 300;

    public static string UnknownOrigin(string value) => $"Origen desconocido: '{value}'. Use RECEIPT o WAREHOUSE.";
    public static string UnknownCause(string value) => $"Causa desconocida: '{value}'.";
    public static string UnknownDisposition(string value) => $"Destino desconocido: '{value}'. Use QUARANTINE o DISCARD.";
    public static string NotQuarantineBin(string code) => $"La posición {code} no es de una zona de cuarentena.";
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

    /// <summary>Nota de los movimientos del ledger: 'DAN-00012 · Llegó dañado · {nota}'.</summary>
    public static string MovementNote(int id, string causeLabel, string? notes)
        => string.IsNullOrWhiteSpace(notes) ? $"{Code(id)} · {causeLabel}" : $"{Code(id)} · {causeLabel} · {notes.Trim()}";
}
