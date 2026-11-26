namespace Teikem.Domain.Clients;

/// <summary>Lote 248 — estado de vigencia de un documento del cliente. Se DERIVA de la fecha (no hay StatusCode ni transiciones).</summary>
public static class ClientDocumentExpiryStates
{
    /// <summary>El tipo no lleva vigencia y el documento no tiene «hasta».</summary>
    public const string NoExpiry = "NO_EXPIRY";
    /// <summary>Tiene «hasta» y faltan más días que el aviso de la compañía.</summary>
    public const string Valid = "VALID";
    /// <summary>Vence dentro del aviso de la compañía (incluido el día que vence).</summary>
    public const string Expiring = "EXPIRING";
    /// <summary>El «hasta» ya pasó.</summary>
    public const string Expired = "EXPIRED";
    /// <summary>Hay otro documento activo del mismo cliente y del mismo tipo que lo reemplaza (<see cref="ClientDocumentRules.IsRenewed"/>).</summary>
    public const string Renewed = "RENEWED";
    /// <summary>El tipo lleva vigencia pero el documento no tiene «hasta» (un documento viejo de un tipo que pasó a «lleva vigencia»).</summary>
    public const string MissingValidTo = "MISSING_VALID_TO";
    public static readonly string[] All = { NoExpiry, Valid, Expiring, Expired, Renewed, MissingValidTo };
}

/// <summary>
/// Lote 248 — un documento visto para decidir si está renovado: id, cliente, tipo y «hasta». Lo arma <c>ClientDocumentQueries.ValidityRowsAsync</c> (la única consulta
/// que comparten la lista «Documentos por vencer», la fuente de datos y el escaneo de avisos).
/// </summary>
public sealed record ClientDocumentValidityKey(int DocumentId, int ClientId, int DocumentTypeLookupId, DateOnly? ValidTo);

/// <summary>Lote 248 — reglas puras y mensajes exactos de los documentos del cliente con vigencia.</summary>
public static class ClientDocumentRules
{
    public const int MaxNumberLength = 60;

    /// <summary>400 — el tipo no existe en el catálogo, está inactivo o la compañía lo apagó en Catálogos de valores.</summary>
    public const string TypeInvalid = "El tipo de documento no existe o está apagado en Catálogos de valores.";
    /// <summary>400.</summary>
    public const string NumberTooLong = "El número del documento no puede pasar de 60 caracteres.";
    /// <summary>400 — el tipo lleva vigencia y falta el «hasta».</summary>
    public const string ValidToRequired = "Este tipo de documento lleva vigencia: escriba la fecha «hasta».";
    /// <summary>400.</summary>
    public const string DatesOrder = "La fecha «hasta» no puede ser anterior a la fecha «desde».";
    /// <summary>409 — quitar un documento que respalda una exención de IVU vigente.</summary>
    public const string BacksExemptionCannotRemove = "Este documento respalda una exención de IVU vigente: dé de baja la exención en Impuestos antes de quitarlo.";
    /// <summary>403 — cambiar número o vigencia de un documento que respalda una exención sin tax.manage.</summary>
    public const string BacksExemptionNeedsTax = "Este documento respalda una exención de IVU: cambiar su número o su vigencia exige el permiso de impuestos (tax.manage).";
    /// <summary>400 — quitarle el «hasta» a un documento que respalda una exención.</summary>
    public const string BacksExemptionNeedsValidTo = "Este documento respalda una exención de IVU: no se le puede quitar la fecha «hasta».";

    /// <summary>Número del documento: recortado; vacío = null. El largo lo valida <see cref="ValidateMetadata"/>.</summary>
    public static string? NormalizeNumber(string? number) => string.IsNullOrWhiteSpace(number) ? null : number.Trim();

    /// <summary>
    /// Valida número y vigencia del estado FINAL del documento (al subir o al editar). Devuelve (campo, mensaje) del primer error, o (null, null) si está bien.
    /// Un «hasta» pasado SÍ se acepta (es el registro de un documento real).
    /// </summary>
    public static (string? Field, string? Error) ValidateMetadata(string? number, DateOnly? validFrom, DateOnly? validTo, ClientDocumentTypeMarks marks)
    {
        var n = NormalizeNumber(number);
        if (n is { Length: > MaxNumberLength }) return ("documentNumber", NumberTooLong);
        if (marks.RequiresValidTo && validTo is null) return ("validTo", ValidToRequired);
        if (validFrom is DateOnly f && validTo is DateOnly t && t < f) return ("validTo", DatesOrder);
        return (null, null);
    }

    /// <summary>Días que faltan para el «hasta» (0 = vence hoy; negativo = ya venció). Sin «hasta» = null.</summary>
    public static int? DaysToExpiry(DateOnly? validTo, DateOnly today) => validTo is DateOnly t ? t.DayNumber - today.DayNumber : null;

    /// <summary>
    /// Estado de vigencia derivado de la fecha. Orden: renovado gana; sin «hasta» = MISSING_VALID_TO si el tipo lleva vigencia y NO_EXPIRY si no; con «hasta»:
    /// vencido si ya pasó, por vencer si faltan <paramref name="warnDays"/> días o menos (incluido el día que vence) y vigente si faltan más.
    /// </summary>
    public static string ExpiryState(DateOnly? validTo, DateOnly today, int warnDays, bool renewed, bool requiresValidTo)
    {
        if (renewed) return ClientDocumentExpiryStates.Renewed;
        if (DaysToExpiry(validTo, today) is not int d) return requiresValidTo ? ClientDocumentExpiryStates.MissingValidTo : ClientDocumentExpiryStates.NoExpiry;
        if (d < 0) return ClientDocumentExpiryStates.Expired;
        return d <= Math.Max(0, warnDays) ? ClientDocumentExpiryStates.Expiring : ClientDocumentExpiryStates.Valid;
    }

    /// <summary>
    /// ¿Otro documento ACTIVO del mismo cliente y del mismo tipo reemplaza a éste? (<paramref name="siblings"/> son documentos activos; el propio se ignora.)
    /// - Documento con «hasta»: lo reemplaza otro con «hasta» posterior; con el mismo «hasta», el subido después (id mayor); o uno sin «hasta» subido después.
    /// - Documento sin «hasta»: lo reemplaza cualquier otro subido después (id mayor).
    /// Un documento sin «hasta» más VIEJO nunca reemplaza a uno con fecha (así un certificado viejo sin datos no apaga los avisos del nuevo).
    /// </summary>
    public static bool IsRenewed(ClientDocumentValidityKey doc, IEnumerable<ClientDocumentValidityKey> siblings)
    {
        foreach (var s in siblings)
        {
            if (s.DocumentId == doc.DocumentId || s.ClientId != doc.ClientId || s.DocumentTypeLookupId != doc.DocumentTypeLookupId) continue;
            var newer = s.DocumentId > doc.DocumentId;
            if (doc.ValidTo is not DateOnly mine) { if (newer) return true; continue; }
            if (s.ValidTo is not DateOnly other) { if (newer) return true; continue; }
            if (other > mine || (other == mine && newer)) return true;
        }
        return false;
    }
}
