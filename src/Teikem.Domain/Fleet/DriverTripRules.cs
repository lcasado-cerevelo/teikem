namespace Teikem.Domain.Fleet;

/// <summary>Monto congelado de un viaje: lo que se guarda en DriverTrip (Amount, DriverTripRateId, RateMissing) y la nota visible.</summary>
public sealed record FrozenTripAmount(decimal Amount, int? RateId, bool RateMissing, string? Note);

/// <summary>
/// Lote 4 (P7): reglas puras del viaje pagado al chofer (DriverTrip).
/// - Freeze: congela la tarifa vigente al crear el viaje; sin tarifa el viaje nace en $0 con RateMissing (R15), coherente
///   con CK_DriverTrip_Rate: (RateMissing = 1 AND DriverTripRateId IS NULL AND Amount = 0) OR (RateMissing = 0 AND DriverTripRateId IS NOT NULL).
/// - Una tarifa capturada en $0 a propósito NO es 'sin tarifa' (RateMissing = 0 y conserva su id).
/// </summary>
public static class DriverTripRules
{
    public const string NoRateNote = "sin tarifa configurada";
    public const int NotesMaxLength = 500;

    public const string TypeRequiredMessage = "El tipo de viaje es obligatorio.";
    public const string FutureDateMessage = "La fecha del viaje no puede ser futura.";
    public const string NotesTooLongMessage = "Las notas del viaje admiten como máximo 500 caracteres.";
    public const string TypeNotFoundLabel = "Tipo de servicio especial";
    public const string TypeInactiveMessage = "El tipo de servicio especial está inactivo; reactívelo o elija otro.";
    public const string TripNotFoundLabel = "Viaje";
    public const string OrderLinkedMessage = "El viaje nace de una entrega especial; cancele la orden o reasigne el chofer.";

    /// <summary>Congela el monto: con tarifa (monto e id) la usa tal cual; si falta cualquiera de los dos, $0 sin tarifa.</summary>
    public static FrozenTripAmount Freeze(decimal? rate, int? rateId)
    {
        if (rate is not decimal amount || rateId is not int id) return new FrozenTripAmount(0m, null, true, NoRateNote);
        return new FrozenTripAmount(amount, id, false, null);
    }

    /// <summary>Espejo de CK_DriverTrip_Rate y CK_DriverTrip_Amount.</summary>
    public static bool SatisfiesRateCheck(decimal amount, int? rateId, bool rateMissing)
        => amount >= 0 && (rateMissing ? rateId is null && amount == 0m : rateId is not null);

    /// <summary>Nota visible del viaje ('sin tarifa configurada' cuando RateMissing).</summary>
    public static string? NoteFor(bool rateMissing) => rateMissing ? NoRateNote : null;

    /// <summary>null si la fecha es válida (hoy o anterior); el mensaje exacto si es futura.</summary>
    public static string? ValidateTripDate(DateOnly date, DateOnly today) => date > today ? FutureDateMessage : null;

    /// <summary>null si las notas caben en DriverTrip.Notes NVARCHAR(500).</summary>
    public static string? ValidateNotes(string? notes) => notes is { Length: > NotesMaxLength } ? NotesTooLongMessage : null;
}
