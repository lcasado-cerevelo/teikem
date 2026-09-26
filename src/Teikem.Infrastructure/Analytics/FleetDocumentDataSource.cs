using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 4 (P3) — fuente FLEET_DOCUMENT: documentos de vehículo, licencias y certificaciones unidos (vista y indicador
/// "Documentos por vencer" de SystemAnalyticsSeeder).
/// - Lee EXCLUSIVAMENTE con FleetQueries.LoadFleetDocumentsAsync (filtro de tenant + regla "vigente por tipo"): solo
///   documentos no superados de dueños activos y no terminales. Estado actual → sin DateField.
/// - ExpiryState/DaysToExpiry con la ventana fija de 30 días (FleetRules.ExpiryState por defecto).
/// - No es una entidad con campos personalizados (EntityTypeCode NULL) y su id es la clave compuesta RowKey
///   ('VD-{id}' | 'DL-{id}' | 'DC-{id}'): ninguna relación ni q.Ids (enteros) puede señalar una fila, así que una consulta
///   por ids devuelve vacío.
/// - Tope ClientDataSourceHelpers.MaxRows, por vencimiento ascendente.
/// </summary>
public sealed class FleetDocumentDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.FleetDocument;
    public string LabelEs => "Documentos de flota";
    public string LabelEn => "Fleet documents";
    public string? EntityTypeCode => null;
    public string? DateField => null;
    public string IdField => "RowKey";
    public string DefaultBusinessModule => BusinessModules.Operations;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("RowKey", "Clave", "Key", DataFieldType.Text),
        new DataField("OwnerKind", "Entidad", "Entity", DataFieldType.Text),
        new DataField("OwnerCode", "Código del dueño", "Owner code", DataFieldType.Text),
        new DataField("OwnerName", "Dueño", "Owner", DataFieldType.Text),
        new DataField("DocumentKind", "Clase de documento", "Document kind", DataFieldType.Text),
        new DataField("DocumentType", "Tipo de documento", "Document type", DataFieldType.Text),
        new DataField("DocNumber", "Número", "Number", DataFieldType.Text),
        new DataField("IssuedDate", "Emitido el", "Issued on", DataFieldType.Date),
        new DataField("ExpiryDate", "Vence el", "Expires on", DataFieldType.Date),
        new DataField("DaysToExpiry", "Días para vencer", "Days to expiry", DataFieldType.Number),
        new DataField("ExpiryState", "Estado de vencimiento", "Expiry state", DataFieldType.Text),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        if (q.Ids is not null) return new List<DataRow>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = await db.LoadFleetDocumentsAsync(today, new FleetDocumentScope(IncludeVehicles: true, IncludeDrivers: true, OnlyActiveOwners: true), ct);

        var selected = rows
            .Where(r => r.OwnerIsActive && !r.OwnerIsTerminal && !r.IsSuperseded)
            .OrderBy(r => r.ExpiryDate ?? DateOnly.MaxValue).ThenBy(r => r.OwnerCode, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.RowKey, StringComparer.Ordinal)
            .Take(ClientDataSourceHelpers.MaxRows)
            .ToList();

        var labels = new Dictionary<int, string>();
        var result = new List<DataRow>(selected.Count);
        foreach (var r in selected)
        {
            result.Add(new DataRow
            {
                ["RowKey"] = r.RowKey,
                ["OwnerKind"] = r.OwnerKind,
                ["OwnerCode"] = r.OwnerCode,
                ["OwnerName"] = r.OwnerName,
                ["DocumentKind"] = FleetDocumentService.KindOf(r),
                ["DocumentType"] = await FleetDocumentService.LabelAsync(lookups, labels, r, tenant.Lang, ct),
                ["DocNumber"] = r.DocNumber,
                ["IssuedDate"] = r.IssuedDate,
                ["ExpiryDate"] = r.ExpiryDate,
                ["DaysToExpiry"] = r.ExpiryDate is DateOnly e ? FleetRules.DaysTo(e, today) : (int?)null,
                ["ExpiryState"] = FleetRules.ExpiryState(r.ExpiryDate, today),
            });
        }
        return result;
    }
}
