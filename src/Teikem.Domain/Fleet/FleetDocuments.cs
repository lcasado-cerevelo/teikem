namespace Teikem.Domain.Fleet;

/// <summary>
/// Fila unificada de documento de flota: documento de vehículo ('VD-{id}'), licencia ('DL-{id}') o certificación
/// ('DC-{id}') con su dueño. DocumentKind es el tipo del documento de vehículo (REGISTRATION, INSURANCE, …) o LICENSE /
/// CERTIFICATION en un chofer; DocTypeCode es el InternalCode del tipo (VehicleDocType, LicenseClass o CertificationType).
/// La arma FleetQueries.LoadFleetDocumentsAsync; la consumen el panel 'Documentos por vencer', la disponibilidad para
/// despacho y la fuente de datos FLEET_DOCUMENT.
/// </summary>
public sealed record FleetDocumentRow(
    string RowKey,
    string OwnerKind,
    int OwnerId,
    Guid OwnerPublicId,
    string OwnerCode,
    string OwnerName,
    bool OwnerIsActive,
    bool OwnerIsTerminal,
    string DocumentKind,
    int DocTypeLookupId,
    string DocTypeCode,
    string? DocNumber,
    DateOnly? IssuedDate,
    DateOnly? ExpiryDate,
    bool IsSuperseded = false);

/// <summary>Reglas puras de documentos de flota.</summary>
public static class FleetDocuments
{
    /// <summary>
    /// Regla 'documento vigente por tipo'. Agrupa por (OwnerKind, OwnerId, DocumentKind, DocTypeCode): un documento con
    /// vencimiento queda IsSuperseded = true si en su grupo existe otro con vencimiento posterior. Los documentos sin
    /// vencimiento nunca superan ni quedan superados; dos con el mismo vencimiento no se superan entre sí. Se espera que
    /// las filas sean de documentos activos (los inactivos no se pasan). Conserva el orden de entrada.
    /// </summary>
    public static IReadOnlyList<FleetDocumentRow> MarkSuperseded(IEnumerable<FleetDocumentRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var list = rows.ToList();
        var latest = list
            .Where(r => r.ExpiryDate.HasValue)
            .GroupBy(Key)
            .ToDictionary(g => g.Key, g => g.Max(r => r.ExpiryDate!.Value));

        return list.Select(r =>
        {
            var superseded = r.ExpiryDate is DateOnly e && latest.TryGetValue(Key(r), out var max) && max > e;
            return r.IsSuperseded == superseded ? r : r with { IsSuperseded = superseded };
        }).ToList();
    }

    private static (string, int, string, string) Key(FleetDocumentRow r)
        => (r.OwnerKind, r.OwnerId, r.DocumentKind, r.DocTypeCode);
}
