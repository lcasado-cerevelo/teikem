namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 248 — aviso de exención de IVU al escoger el cliente de una orden de venta y en el resumen
// (GET /api/v1/sales-orders/tax-notice?client={publicId}; sales.create|sales.view; módulo SALES_ORDERS). No bloquea nada.
// ======================================================================================================================

/// <summary>Niveles del aviso de exención al vender.</summary>
public static class SalesTaxNoticeLevels
{
    /// <summary>Nada que avisar (sin exenciones, todo vigente fuera del aviso de la compañía, o módulo TAX apagado).</summary>
    public const string None = "NONE";
    /// <summary>La exención vigente vence dentro del aviso de la compañía (el mayor umbral positivo de Tenant.DocExpiryThresholdDays), incluido el día que vence.</summary>
    public const string Expiring = "EXPIRING";
    /// <summary>Una exención venció y nada vigente cubre lo que cubría: se cobrará el IVU.</summary>
    public const string Expired = "EXPIRED";
}

/// <summary>
/// El aviso. Level: NONE, EXPIRING o EXPIRED. Message: el texto en español con la fecha en el formato de la compañía (null con NONE); la web arma el suyo con
/// t() a partir de Level, ValidTo y Days. ValidTo: la fecha en que vence o venció. Days: días que faltan (EXPIRING, 0 = vence hoy) o días negativos desde que
/// venció (EXPIRED). CertificateNumber y DocumentTypeLabel: los del documento que respalda la exención (etiqueta en el idioma del usuario).
/// </summary>
public sealed record SalesTaxNoticeDto(string Level, string? Message, DateOnly? ValidTo, int? Days, string? CertificateNumber, string? DocumentTypeLabel)
{
    public static SalesTaxNoticeDto None { get; } = new(SalesTaxNoticeLevels.None, null, null, null, null, null);
}
