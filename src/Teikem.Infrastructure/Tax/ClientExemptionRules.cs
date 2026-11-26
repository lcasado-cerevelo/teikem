namespace Teikem.Infrastructure.Tax;

/// <summary>
/// Lote 248 — reglas puras y mensajes exactos de la exención de IVU atada al documento del cliente que la respalda (certificado). La exención nueva siempre
/// nace de un documento; la vigencia y el número son los del documento (<see cref="ClientExemptionBacking"/>).
/// </summary>
public static class ClientExemptionRules
{
    /// <summary>400 — la exención no trae documento (o es vieja y se quiere editar o reactivar sin vincularle uno).</summary>
    public const string DocumentRequired = "La exención necesita el documento que la respalda.";
    /// <summary>400 — la captura única trae archivo nuevo Y documento existente.</summary>
    public const string FileAndDocument = "Mande el archivo del certificado o escoja un documento ya subido, no las dos cosas.";
    /// <summary>400 — la captura única trae un archivo vacío.</summary>
    public const string FileRequired = "Adjunte el archivo del certificado.";
    /// <summary>400 — el tipo del documento no tiene la marca «es de impuestos».</summary>
    public const string DocumentNotTax = "Ese tipo de documento no es de impuestos: no puede respaldar una exención.";
    /// <summary>400 — el documento se quitó (IsActive = 0).</summary>
    public const string DocumentInactive = "El documento que respaldaba la exención se quitó: suba el certificado vigente.";
    /// <summary>400.</summary>
    public const string DocumentWithoutNumber = "El documento no tiene número: escríbalo en el documento antes de aplicar la exención.";
    /// <summary>400.</summary>
    public const string DocumentWithoutValidTo = "El documento no tiene fecha «hasta»: la exención necesita la vigencia del documento.";
    /// <summary>400 — el «hasta» del documento ya pasó.</summary>
    public const string DocumentExpired = "El documento ya venció: suba el certificado vigente.";
    /// <summary>400 — captura única sin «desde».</summary>
    public const string ValidFromRequired = "Escriba desde cuándo vale el certificado.";
    /// <summary>400 — captura única sin «hasta».</summary>
    public const string ValidToRequired = "Escriba hasta cuándo vale el certificado.";
    /// <summary>400 — captura única con «hasta» ya pasado.</summary>
    public const string ValidToPast = "La fecha «hasta» ya pasó: la exención nacería vencida.";
    /// <summary>409 — el documento ya respalda otra exención activa (también lo garantiza UX_ClientTaxExemption_Document).</summary>
    public const string DocumentAlreadyBacks = "Ese documento ya respalda otra exención activa.";
    /// <summary>400 — PATCH de fechas o número de una exención vinculada.</summary>
    public const string DatesFollowDocument = "La vigencia y el número de la exención son los del documento: corríjalos en Documentos del cliente.";
    /// <summary>404 — documento de otro cliente o de otra compañía (NotFoundException("Documento")).</summary>
    public const string DocumentNotFound = "Documento no encontrado.";

    /// <summary>Tope del tipo de exención (columna ExemptionType NVARCHAR(60)).</summary>
    public const int MaxExemptionTypeLength = 60;

    /// <summary>
    /// ¿Puede ese documento respaldar una exención HOY? Devuelve (campo, mensaje) del primer problema, o (null, null). Orden: quitado, no es de impuestos, sin número,
    /// sin «hasta», vencido (el último día de vigencia todavía sirve).
    /// </summary>
    public static (string? Field, string? Error) ValidateBackingDocument(bool isActive, bool isTax, string? number, DateOnly? validTo, DateOnly today)
    {
        const string field = "clientDocumentPublicId";
        if (!isActive) return (field, DocumentInactive);
        if (!isTax) return (field, DocumentNotTax);
        if (string.IsNullOrWhiteSpace(number)) return (field, DocumentWithoutNumber);
        if (validTo is not DateOnly to) return (field, DocumentWithoutValidTo);
        if (to < today) return (field, DocumentExpired);
        return (null, null);
    }

    /// <summary>
    /// El tipo de exención: lo que escribió el usuario (su largo lo valida el servicio) o, si no escribió nada, la etiqueta del tipo de documento recortada al tope
    /// de la columna.
    /// </summary>
    public static string DefaultExemptionType(string? requested, string? typeLabel)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return requested.Trim();
        var t = (typeLabel ?? string.Empty).Trim();
        return t.Length > MaxExemptionTypeLength ? t[..MaxExemptionTypeLength].TrimEnd() : t;
    }
}
