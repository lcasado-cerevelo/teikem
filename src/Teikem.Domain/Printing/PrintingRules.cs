using Teikem.Domain.Constants;

namespace Teikem.Domain.Printing;

/// <summary>
/// Lote 43 (F-G1) — reglas puras de impresoras y plantillas de label, con los mensajes EXACTOS que citan el manual 12 y la FAQ.
/// Cada validación devuelve (valor normalizado, null) o (null, mensaje de 400); los 409 y 404 los lanza el servicio.
/// </summary>
public static class PrintingRules
{
    public const int NameMaxLength = 80;
    public const int ModelMaxLength = 80;
    public const int NotesMaxLength = 300;
    public const int SystemNameMaxLength = 200;

    // ---------------- impresoras: 400 ----------------
    public const string PrinterNameRequiredMessage = "Escriba el nombre de la impresora.";
    public const string NameTooLongMessage = "El nombre no puede exceder 80 caracteres.";
    public const string ModelTooLongMessage = "El modelo no puede exceder 80 caracteres.";
    public const string NotesTooLongMessage = "La nota no puede exceder 300 caracteres.";
    public const string PurposeRequiredMessage = "Indique el uso de la impresora: LABEL (labels) o DOCUMENT (documentos).";
    public const string PurposeImmutableMessage = "El uso de la impresora no se puede cambiar; cree otra impresora.";
    public const string SystemNameTooLongMessage = "El nombre en el sistema no puede exceder 200 caracteres.";
    public const string LabelMediaMessage = "Una impresora de labels usa etiquetas de 4X6, 4X4 o 4X2.";
    public const string DocumentMediaMessage = "Una impresora de documentos usa papel carta (LETTER).";

    /// <summary>400 «Conexión de impresora desconocida: 'X'. Use BROWSER (PDF del navegador) o QZ_TRAY (directo con QZ Tray).»</summary>
    public static string UnknownConnectionMessage(string? connection)
        => $"Conexión de impresora desconocida: '{connection}'. Use BROWSER (PDF del navegador) o QZ_TRAY (directo con QZ Tray).";

    /// <summary>400 «Uso de impresora desconocido: 'X'.»</summary>
    public static string UnknownPurposeMessage(string? purpose) => $"Uso de impresora desconocido: '{purpose}'.";

    // ---------------- impresoras: 409 ----------------
    public const string DuplicatePrinterMessage = "Ya existe una impresora activa con ese nombre.";
    public const string PrinterInactiveDefaultMessage = "La impresora está inactiva; reactívela antes de hacerla predeterminada.";

    // ---------------- plantillas: 400 y 409 ----------------
    public const string TemplateNameRequiredMessage = "Escriba el nombre de la plantilla.";
    public const string DuplicateTemplateMessage = "Ya existe una plantilla de label activa con ese nombre.";
    public const string TemplateInactiveDefaultMessage = "La plantilla está inactiva; reactívela antes de hacerla predeterminada.";

    /// <summary>Nombre obligatorio, recortado, de hasta 80 caracteres.</summary>
    public static (string? Value, string? Error) NormalizeName(string? raw, string requiredMessage)
    {
        var v = raw?.Trim();
        if (string.IsNullOrEmpty(v)) return (null, requiredMessage);
        if (v.Length > NameMaxLength) return (null, NameTooLongMessage);
        return (v, null);
    }

    /// <summary>Texto opcional recortado ("" o solo espacios = sin valor) con su máximo.</summary>
    public static (string? Value, string? Error) NormalizeOptional(string? raw, int maxLength, string tooLongMessage)
    {
        var v = raw?.Trim();
        if (string.IsNullOrEmpty(v)) return (null, null);
        return v.Length > maxLength ? (null, tooLongMessage) : (v, null);
    }

    /// <summary>Uso obligatorio: LABEL o DOCUMENT (sin distinguir mayúsculas).</summary>
    public static (string? Value, string? Error) NormalizePurpose(string? raw)
    {
        var v = raw?.Trim();
        if (string.IsNullOrEmpty(v)) return (null, PurposeRequiredMessage);
        var upper = v.ToUpperInvariant();
        return PrinterPurposes.All.Contains(upper) ? (upper, null) : (null, UnknownPurposeMessage(v));
    }

    /// <summary>Conexión: vacía = BROWSER; BROWSER o QZ_TRAY (sin distinguir mayúsculas; "QZ-TRAY" y "QZ TRAY" valen); otra es 400.</summary>
    public static (string? Value, string? Error) NormalizeConnection(string? raw)
    {
        var v = raw?.Trim();
        if (string.IsNullOrEmpty(v)) return (PrinterConnections.Browser, null);
        var upper = v.ToUpperInvariant().Replace('-', '_').Replace(' ', '_');
        return PrinterConnections.All.Contains(upper) ? (upper, null) : (null, UnknownConnectionMessage(v));
    }

    /// <summary>
    /// Tamaño según el uso: vacío = el de siempre (4X6 para labels, LETTER para documentos); "4x6" y "4×6" valen como 4X6. Un tamaño que
    /// no es del uso es 400 con el mensaje del uso.
    /// </summary>
    public static (string? Value, string? Error) NormalizeMedia(string purpose, string? raw)
    {
        var isLabel = purpose == PrinterPurposes.Label;
        var v = raw?.Trim();
        if (string.IsNullOrEmpty(v)) return (isLabel ? PrintMediaSizes.Label4x6 : PrintMediaSizes.Letter, null);
        var upper = v.Replace('×', 'X').Replace(" ", string.Empty).ToUpperInvariant();
        if (isLabel) return PrintMediaSizes.LabelSizes.Contains(upper) ? (upper, null) : (null, LabelMediaMessage);
        return upper == PrintMediaSizes.Letter ? (upper, null) : (null, DocumentMediaMessage);
    }

    /// <summary>¿El uso pedido en una edición cambia el de la impresora? (null = sin cambio; el mismo, sin distinguir mayúsculas, tampoco).</summary>
    public static bool ChangesPurpose(string current, string? requested)
        => !string.IsNullOrWhiteSpace(requested) && !string.Equals(requested.Trim(), current, StringComparison.OrdinalIgnoreCase);
}
