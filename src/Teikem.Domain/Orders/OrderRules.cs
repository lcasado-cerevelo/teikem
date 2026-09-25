using Teikem.Domain.Clients;

namespace Teikem.Domain.Orders;

/// <summary>Resultado del chequeo de factura repetida por consignatario (R36).</summary>
public enum DuplicateInvoiceDecision
{
    /// <summary>Se puede crear la orden (no se tecleó factura, no existe otra orden con ella, o el operador ya confirmó).</summary>
    Ok,
    /// <summary>El consignatario no admite facturas repetidas: 409 sin posibilidad de confirmar.</summary>
    Blocked,
    /// <summary>El consignatario admite repetidas pero el operador aún no confirmó: 409 confirmable ('Crear de todos modos').</summary>
    NeedsConfirmation,
}

/// <summary>Línea de paquete tal como llega de la captura (entrada pura para las reglas; sin catálogos).</summary>
public sealed record PackageLineInput(string? Description, int Pieces = 1, decimal? WeightKg = null, decimal? VolumeM3 = null);

/// <summary>
/// Reglas puras de la captura de órdenes (Lote 3, P2): validación de líneas de paquete, totales, resumen 'Caja ×2 + Sobre ×1',
/// decisión de factura repetida (R36), campos fijos que el PATCH/POST rechazan, eliminación solo en la etapa inicial y el
/// snapshot de la parada a partir de la Location (L236). Sin EF ni servicios: se prueba con xunit; el servicio traduce a 400/409/422.
/// </summary>
public static class OrderRules
{
    public const int DescriptionMaxLength = 250;
    public const int StopNotesMaxLength = 500;
    public const int ReferenceValueMaxLength = 120;
    public const int ReferenceSourceMaxLength = 80;
    public const string DefaultCountryCode = "PR";

    public const string PackagesRequiredMessage = "Indique al menos una línea de paquete.";
    public const string SpecialDeliveryNoCargoMessage = "Una entrega especial no lleva líneas de paquete ni COD.";
    public const string PiecesMessage = "La cantidad de piezas debe ser al menos 1.";
    public const string CodNegativeMessage = "El monto COD no puede ser negativo.";
    public const string CodTypeOnCaptureMessage = "El tipo de COD se registra al entregar, no en la captura.";
    public const string PackBatchAlwaysTeikemMessage = "El número de empaque siempre lo genera Teikem.";
    public const string DeleteOnlyInitialMessage = "Solo se puede eliminar una orden en su estatus inicial; use la cancelación.";

    /// <summary>Campos que se fijan al crear la orden y el PATCH rechaza con 400 (L1065; DECISIÓN 15).</summary>
    public static readonly IReadOnlyList<string> FixedFieldsOnPatch = new[] { "clientPublicId", "orderNumber", "clientInvoiceNumber", "packBatchNumber" };

    /// <summary>Campos que el POST rechaza con 400: el empaque siempre lo genera Teikem y el tipo de COD se registra al entregar.</summary>
    public static readonly IReadOnlyList<string> ForbiddenOnCreate = new[] { "packBatchNumber", "codType" };

    /// <summary>FixedFieldsOnPatch ∪ {codType}: lo que el PATCH rechaza.</summary>
    public static readonly IReadOnlyList<string> ForbiddenOnPatch = FixedFieldsOnPatch.Concat(new[] { "codType" }).ToArray();

    // ---------------------------------------------------------------- paquetes

    /// <summary>
    /// Errores por campo de las líneas de paquete. Una orden normal exige al menos una línea; una entrega especial no admite
    /// líneas ni COD (su única CargoLine la crea el servicio con el nombre del servicio especial).
    /// </summary>
    public static IReadOnlyDictionary<string, string> ValidatePackages(IReadOnlyList<PackageLineInput>? lines, bool isSpecialDelivery, decimal? codAmount = null)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var count = lines?.Count ?? 0;

        if (isSpecialDelivery)
        {
            if (count > 0) errors["packages"] = SpecialDeliveryNoCargoMessage;
            if (codAmount is > 0) errors["codAmount"] = SpecialDeliveryNoCargoMessage;
            return errors;
        }

        if (count == 0)
        {
            errors["packages"] = PackagesRequiredMessage;
            return errors;
        }

        for (var i = 0; i < count; i++)
        {
            var line = lines![i];
            if (line.Pieces < 1) errors[$"packages[{i}].pieces"] = PiecesMessage;
            if (line.Description is not null && line.Description.Trim().Length > DescriptionMaxLength)
                errors[$"packages[{i}].description"] = $"Máximo {DescriptionMaxLength} caracteres.";
            if (line.WeightKg < 0) errors[$"packages[{i}].weightKg"] = "El peso no puede ser negativo.";
            if (line.VolumeM3 < 0) errors[$"packages[{i}].volumeM3"] = "El volumen no puede ser negativo.";
        }
        return errors;
    }

    /// <summary>Totales de cabecera: piezas suman; peso y volumen suman solo si alguna línea trae el dato (null si ninguna).</summary>
    public static (int Pieces, decimal? WeightKg, decimal? VolumeM3) Totals(IEnumerable<PackageLineInput> lines)
    {
        var pieces = 0;
        decimal? weight = null, volume = null;
        foreach (var l in lines)
        {
            pieces += l.Pieces;
            if (l.WeightKg is decimal w) weight = (weight ?? 0m) + w;
            if (l.VolumeM3 is decimal v) volume = (volume ?? 0m) + v;
        }
        return (pieces, weight, volume);
    }

    /// <summary>'Caja ×2 + Sobre ×1': consolida por etiqueta (sin distinguir mayúsculas) en orden de aparición. Vacío → "".</summary>
    public static string PackagesSummary(IEnumerable<(string Label, int Pieces)> lines)
    {
        var order = new List<string>();
        var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (label, pieces) in lines)
        {
            var key = string.IsNullOrWhiteSpace(label) ? "?" : label.Trim();
            if (!totals.ContainsKey(key)) { totals[key] = 0; order.Add(key); }
            totals[key] += pieces;
        }
        return string.Join(" + ", order.Select(k => $"{k} ×{totals[k]}"));
    }

    // ---------------------------------------------------------------- factura repetida (R36)

    /// <summary>
    /// Solo se evalúa cuando el número de factura fue TECLEADO y contra el mismo consignatario. Con AllowDupInvoice=0 se bloquea;
    /// con AllowDupInvoice=1 se pide confirmación una vez ('Crear de todos modos'); confirmado → Ok.
    /// </summary>
    public static DuplicateInvoiceDecision DecideDuplicateInvoice(bool typedByClient, string? existingOrderNumber, bool allowDupInvoice, bool confirmed)
    {
        if (!typedByClient || string.IsNullOrWhiteSpace(existingOrderNumber)) return DuplicateInvoiceDecision.Ok;
        if (!allowDupInvoice) return DuplicateInvoiceDecision.Blocked;
        return confirmed ? DuplicateInvoiceDecision.Ok : DuplicateInvoiceDecision.NeedsConfirmation;
    }

    // ---------------------------------------------------------------- campos fijos / prohibidos

    /// <summary>
    /// Primer campo ofensivo entre las llaves extra del JSON (comparación sin distinguir mayúsculas) con su mensaje, o null.
    /// El nombre devuelto es el canónico de la lista prohibida (p. ej. 'orderNumber' aunque llegara 'OrderNumber').
    /// </summary>
    public static (string Field, string Message)? RejectExtraFields(IEnumerable<string>? extraKeys, IEnumerable<string> forbidden)
    {
        if (extraKeys is null) return null;
        var forbiddenList = forbidden as IReadOnlyList<string> ?? forbidden.ToList();
        foreach (var key in extraKeys)
        {
            var hit = forbiddenList.FirstOrDefault(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
            if (hit is null) continue;
            return (hit, MessageForForbidden(hit));
        }
        return null;
    }

    private static string MessageForForbidden(string field)
    {
        if (string.Equals(field, "codType", StringComparison.OrdinalIgnoreCase)) return CodTypeOnCaptureMessage;
        if (string.Equals(field, "packBatchNumber", StringComparison.OrdinalIgnoreCase)) return PackBatchAlwaysTeikemMessage;
        return $"El campo '{field}' se fija al crear la orden y no se edita.";
    }

    // ---------------------------------------------------------------- eliminación / COD

    /// <summary>Eliminar (baja lógica) solo en la etapa inicial y solo si sigue activa (DECISIÓN 6).</summary>
    public static bool CanDelete(bool isInitialStatus, bool isActive) => isInitialStatus && isActive;

    /// <summary>Mensaje de error del monto COD o null si es válido (null y 0 = sin COD).</summary>
    public static string? ValidateCod(decimal? amount) => amount < 0 ? CodNegativeMessage : null;

    // ---------------------------------------------------------------- snapshot de parada (L236)

    /// <summary>
    /// Copia a la parada la dirección de la Location (Snap*), el país (código ISO; 'PR' si no hay), los minutos de servicio,
    /// las notas de entrega recortadas a 500 y el LocationId. Ventana horaria: solo si hay RequestedDate y la Location tiene
    /// ventana por defecto (RequestedDate.Date + hora, sin conversión de zona horaria; DECISIÓN 27); sin RequestedDate queda null.
    /// </summary>
    public static void ApplySnapshot(OrderStop stop, Location loc, string? countryCode, DateTime? requestedDate)
    {
        stop.LocationId = loc.LocationId;
        stop.SnapName = loc.Name;
        stop.SnapLine1 = loc.Line1;
        stop.SnapLine2 = loc.Line2;
        stop.SnapCity = loc.City;
        stop.SnapState = loc.State;
        stop.SnapPostalCode = loc.PostalCode;
        stop.SnapCountryCode = string.IsNullOrWhiteSpace(countryCode) ? DefaultCountryCode : countryCode.Trim().ToUpperInvariant();
        stop.ServiceMinutes = loc.DefaultServiceMinutes;
        stop.Notes = Truncate(loc.DeliveryNotes, StopNotesMaxLength);
        ApplyWindow(stop, loc, requestedDate);
    }

    /// <summary>Solo la ventana horaria (se reaplica cuando cambia la fecha solicitada sin cambiar de consignatario).</summary>
    public static void ApplyWindow(OrderStop stop, Location loc, DateTime? requestedDate)
    {
        stop.WindowStartUtc = requestedDate is DateTime ds && loc.DefaultWindowStart is TimeOnly s ? ds.Date.Add(s.ToTimeSpan()) : null;
        stop.WindowEndUtc = requestedDate is DateTime de && loc.DefaultWindowEnd is TimeOnly e ? de.Date.Add(e.ToTimeSpan()) : null;
    }

    private static string? Truncate(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        return t.Length <= max ? t : t[..max];
    }
}
