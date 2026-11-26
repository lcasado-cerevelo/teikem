using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Notifications;

/// <summary>
/// Lote 248 — códigos de los eventos de aviso de los documentos del cliente con vigencia. Con el prefijo SALES_ caen en el grupo «Ventas» de Reglas de aviso
/// (<see cref="NotificationEventCategories.Of"/>) sin tocar NotificationEvents.cs. Hasta 60 caracteres (columna EventCode).
/// </summary>
public static class ClientDocumentEventCodes
{
    public const string Expiring = "SALES_CLIENT_DOCUMENT_EXPIRING";
    public const string Expired = "SALES_CLIENT_DOCUMENT_EXPIRED";

    public static readonly string[] All = { Expiring, Expired };
}

/// <summary>Lote 248 — claves de los destinatarios del evento (Kind CONTEXT) de los avisos de documentos del cliente.</summary>
public static class ClientDocumentContextRecipients
{
    /// <summary>«Vendedores del cliente (ruta y preferido)»: SalesSellerQueries.NoticeUsersAsync.</summary>
    public const string ClientSellers = "CLIENT_SELLERS";
    /// <summary>«Cliente del documento»: correo principal, contactos marcados «Recibe avisos por correo» y los correos extra del aviso DOCUMENT_EXPIRING del cliente.</summary>
    public const string Client = "CLIENT";
}

/// <summary>
/// Lote 248 — los eventos «Documento del cliente por vencer» y «Documento del cliente vencido». Los publica el trabajo diario DOCUMENT_EXPIRY_SCAN
/// (<see cref="Services.DocumentExpiryScanService"/>), uno por documento y umbral (la clave de no repetición lleva la fecha «hasta»). El contenido y los destinatarios
/// del evento los arma <see cref="ClientDocumentNoticeContent"/> al enviar. Son opcionales; las reglas las arma el administrador en Sistema → Reglas de aviso
/// (no se siembra ninguna). Los documentos del cliente viven en el módulo de catálogo.
/// </summary>
public sealed class ClientDocumentEventProvider : INotificationEventProvider
{
    public const string LinkTemplate = "/clients?client={clientPublicId}&tab=documents";

    private static NotificationVariable[] Variables(string daysEs, string daysEn, string daysSample) => new[]
    {
        new NotificationVariable("client", "Cliente", "Customer", "Farmacia Central"),
        new NotificationVariable("clientCode", "Número del cliente", "Customer number", "FARMACIA-CE"),
        new NotificationVariable("documentType", "Tipo de documento", "Document type", "Certificado de exención de IVU estatal"),
        new NotificationVariable("documentNumber", "Número del documento", "Document number", "E-12345"),
        new NotificationVariable("validTo", "Vence (fecha «hasta»)", "Expires (valid to)", "12/31/2026"),
        new NotificationVariable("days", daysEs, daysEn, daysSample),
        new NotificationVariable("threshold", "Umbral que se cruzó (días)", "Threshold crossed (days)", "15"),
        new NotificationVariable("taxNote", "Nota de la exención de IVU", "Sales tax exemption note", "Respalda una exención de IVU: al vencer, las ventas vuelven a llevar IVU."),
        new NotificationVariable("clientPublicId", "Identificador del cliente", "Customer identifier", "0f8fad5bd9cb469fa16570867728950e"),
        new NotificationVariable("documentPublicId", "Identificador del documento", "Document identifier", "7c9e6679742540de944be07fc1f90ae7"),
        new NotificationVariable("validToIso", "Fecha «hasta» (AAAA-MM-DD)", "Valid to (YYYY-MM-DD)", "2026-12-31"),
    };

    private static NotificationContextRecipientDef[] Recipients() => new[]
    {
        new NotificationContextRecipientDef(ClientDocumentContextRecipients.ClientSellers, "Vendedores del cliente (ruta y preferido)", "Customer's sales reps (route and preferred)", IsDefault: true),
        new NotificationContextRecipientDef(ClientDocumentContextRecipients.Client, "Cliente del documento (correo principal y contactos marcados)",
            "Document's customer (main email and flagged contacts)", IsDefault: true, Audience: NotificationAudience.Customer),
    };

    public IEnumerable<NotificationEventDef> Events { get; } = new[]
    {
        new NotificationEventDef(
            ClientDocumentEventCodes.Expiring, ModuleKeys.Catalog, "Documento del cliente por vencer", "Customer document about to expire",
            Variables("Días que faltan", "Days left", "15"),
            IsOptional: true, LinkTemplate: LinkTemplate,
            "Documento por vencer: {documentType} de {client}", "Document about to expire: {documentType} for {client}",
            "El documento {documentType} {documentNumber} de {client} ({clientCode}) vence el {validTo}: quedan {days} día(s). {taxNote}",
            "The {documentType} document {documentNumber} for {client} ({clientCode}) expires on {validTo}: {days} day(s) left. {taxNote}",
            ContextRecipients: Recipients()),
        new NotificationEventDef(
            ClientDocumentEventCodes.Expired, ModuleKeys.Catalog, "Documento del cliente vencido", "Customer document expired",
            Variables("Días desde que venció", "Days since it expired", "3"),
            IsOptional: true, LinkTemplate: LinkTemplate,
            "Documento vencido: {documentType} de {client}", "Document expired: {documentType} for {client}",
            "El documento {documentType} {documentNumber} de {client} ({clientCode}) venció el {validTo}, hace {days} día(s). {taxNote}",
            "The {documentType} document {documentNumber} for {client} ({clientCode}) expired on {validTo}, {days} day(s) ago. {taxNote}",
            ContextRecipients: Recipients()),
    };
}
