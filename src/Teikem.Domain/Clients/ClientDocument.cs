using Teikem.Domain.Common;

namespace Teikem.Domain.Clients;

/// <summary>Lote 222 — constantes de los documentos del cliente y del alta mínima (prospecto).</summary>
public static class ClientDocumentCodes
{
    /// <summary>EntityType de la bitácora (sembrado en la sección 2026-11-07 de update.sql).</summary>
    public const string EntityType = "CLIENT_DOCUMENT";
    /// <summary>Catálogo LookupCode del tipo de documento.</summary>
    public const string TypeDomain = "ClientDocumentType";
    public const string MerchantRegistration = "MERCHANT_REGISTRATION";
    public const string ResellerCertificate = "RESELLER_CERTIFICATE";
    public const string Other = "OTHER";
    public static readonly string[] Types = { MerchantRegistration, ResellerCertificate, Other };
}

/// <summary>
/// Lote 222 (plan del pedido de vendedores §1.3) — un documento del cliente: la foto del registro de comerciante, el certificado de revendedor u otro. El contenido es un
/// StoredFile (clase DOCUMENT, dueño CLIENT) cifrado en el almacén de archivos; esta fila dice de qué cliente es y qué documento es. Quitarlo = IsActive = 0 (nunca DELETE).
/// </summary>
[AuditEntity(ClientDocumentCodes.EntityType)]
public class ClientDocument : ITenantScoped, ISoftDeletable
{
    public int ClientDocumentId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    public int StoredFileId { get; set; }
    public int DocumentTypeLookupId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
}

/// <summary>Lote 222 — reglas puras y mensajes del cliente prospecto (alta mínima, aprobar y rechazar) y de sus documentos.</summary>
public static class ClientProspectRules
{
    public const int MaxMerchantRegistrationLength = 50;
    public const int MaxNotesLength = 300;

    public const string NotProspect = "El cliente no está pendiente de aprobación.";
    public const string RejectNeedsComment = "Escriba por qué se rechaza el cliente.";
    public const string ProspectLeavesByApproval = "Un cliente prospecto se aprueba o se rechaza con «Aprobar» o «Rechazar» (permiso sales.approve_client).";
    public const string ContactRequired = "El nombre del contacto es obligatorio.";
    public const string PhoneRequired = "El teléfono es obligatorio.";
    public const string PhoneTenDigits = "El teléfono debe tener 10 dígitos, sin espacios ni guiones.";
    public const string ExtensionInvalid = "La extensión admite solo dígitos (hasta 6).";
    public const string AddressRequired = "La dirección física es obligatoria.";
    public const string MerchantRegistrationTooLong = "El registro de comerciante no puede pasar de 50 caracteres.";
    public const string DocumentTypeInvalid = "El tipo de documento debe ser MERCHANT_REGISTRATION, RESELLER_CERTIFICATE u OTHER.";
    public const string DocumentNotesTooLong = "La nota del documento no puede pasar de 300 caracteres.";
    public const string DocumentNeedsPermission = "Subir documentos de este cliente exige el permiso de editar clientes (clients.update).";

    /// <summary>Teléfono del alta mínima: exactamente 10 dígitos (como el formulario de los vendedores). Devuelve (dígitos, error).</summary>
    public static (string? Digits, string? Error) NormalizePhone10(string? value, bool required)
    {
        if (string.IsNullOrWhiteSpace(value)) return required ? (null, PhoneRequired) : (null, null);
        var v = value.Trim();
        return v.Length == 10 && v.All(char.IsAsciiDigit) ? (v, null) : (null, PhoneTenDigits);
    }

    /// <summary>Extensión: solo dígitos, hasta 6; vacía = null.</summary>
    public static (string? Ext, string? Error) NormalizeExtension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var v = value.Trim();
        return v.Length <= 6 && v.All(char.IsAsciiDigit) ? (v, null) : (null, ExtensionInvalid);
    }

    /// <summary>SURI: recortado, vacío = null, hasta 50.</summary>
    public static (string? Value, string? Error) NormalizeMerchantRegistration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var v = value.Trim();
        return v.Length > MaxMerchantRegistrationLength ? (null, MerchantRegistrationTooLong) : (v, null);
    }

    /// <summary>El comentario que deja constancia de la aprobación: el límite de crédito y el término con que quedó, y lo que escribió quien aprueba.</summary>
    public static string ApprovalComment(string approver, decimal? creditLimit, string? termLabel, string? comment)
    {
        var limit = creditLimit is decimal l ? l.ToString("$#,##0.00", System.Globalization.CultureInfo.InvariantCulture) : "sin límite";
        var text = $"Aprobado por {approver}: límite de crédito {limit}, término {(string.IsNullOrWhiteSpace(termLabel) ? "sin término" : termLabel)}.";
        return string.IsNullOrWhiteSpace(comment) ? text : $"{text} {comment.Trim()}";
    }

    public static string RejectionComment(string rejecter, int cancelledOrders, string comment)
        => $"Rechazado por {rejecter} ({cancelledOrders} orden(es) abierta(s) cancelada(s)): {comment.Trim()}";
}
