namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 248 (Lote D del plan) — lista «Documentos por vencer» (GET /api/v1/client-documents/expiring).
// ======================================================================================================================

/// <summary>Lote 248 — qué es cada fila de «Documentos por vencer».</summary>
public static class ExpiringClientDocumentRowKinds
{
    /// <summary>Un documento del cliente (con su archivo).</summary>
    public const string Document = "DOCUMENT";
    /// <summary>Una exención de IVU vieja sin documento («falta el documento»): solo la ve quien tiene tax.manage con el módulo TAX encendido.</summary>
    public const string ExemptionWithoutDocument = "EXEMPTION_WITHOUT_DOCUMENT";
}

/// <summary>
/// Lote 248 — filtros de la lista. WithinDays (1 a 365) = documentos que vencen de hoy a hoy + N días; null = el aviso de la compañía (mayor umbral positivo de
/// Tenant.DocExpiryThresholdDays). IncludeExpired = también los ya vencidos (sin límite hacia atrás); IncludeMissing = también los de un tipo con vigencia que no tienen
/// «hasta». ClientPublicId = solo ese cliente; Q = busca en código y nombre del cliente, tipo y número.
/// </summary>
public sealed record ExpiringClientDocumentsQuery(int? WithinDays = null, bool IncludeExpired = true, bool IncludeMissing = true, Guid? ClientPublicId = null, string? Q = null);

/// <summary>
/// Lote 248 — una fila de «Documentos por vencer». RowKind DOCUMENT: un documento (DocumentPublicId, FilePublicId). RowKind EXEMPTION_WITHOUT_DOCUMENT: una exención vieja
/// sin documento (ExemptionPublicId; DocumentTypeLabel = el tipo de exención escrito a mano; DocumentNumber = el número del certificado). ExpiryState sale de la fecha
/// (VALID, EXPIRING, EXPIRED, MISSING_VALID_TO, NO_EXPIRY); los renovados no salen. DocumentTypeLabel en el idioma de quien consulta.
/// </summary>
public sealed record ExpiringClientDocumentDto(
    string RowKind,
    Guid? DocumentPublicId,
    Guid? ExemptionPublicId,
    Guid ClientPublicId,
    string ClientCode,
    string ClientName,
    string? DocumentType,
    string DocumentTypeLabel,
    string? DocumentNumber,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    int? DaysToExpiry,
    string ExpiryState,
    bool IsTax,
    bool RequiresValidTo,
    bool BacksExemption,
    Guid? FilePublicId,
    DateTime CreatedAtUtc);

/// <summary>Lote 248 — la lista: WarnDays (aviso de la compañía), WithinDays (la ventana que se usó), Truncated = había más de 2000 filas (se cortó).</summary>
public sealed record ExpiringClientDocumentsDto(int WarnDays, int WithinDays, bool Truncated, IReadOnlyList<ExpiringClientDocumentDto> Items);
