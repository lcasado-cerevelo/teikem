using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 248 (Lote D del plan) — fuente CLIENT_DOCUMENT, «Documentos de clientes»: una fila por documento ACTIVO (con su archivo) de los clientes activos, con su vigencia
/// de HOY (estado actual, sin DateField, como FLEET_DOCUMENT): días para vencer y estado (VALID, EXPIRING, EXPIRED, RENEWED, NO_EXPIRY, MISSING_VALID_TO; «por vencer» con
/// el aviso de la compañía), si el tipo es de impuestos y si respalda una exención de IVU activa.
/// - Lee EXCLUSIVAMENTE con <see cref="ClientDocumentQueries.ValidityRowsAsync"/> (la misma definición de «renovado» que la lista «Documentos por vencer» y el escaneo).
/// - Sin campos personalizados (EntityTypeCode null); IdField DocumentId (q.Ids filtra por documento); relación Client por ClientId.
/// - Día local de la compañía (ITenantClock) y tope ClientDataSourceHelpers.MaxRows, por «hasta» ascendente.
/// - Lectura: clients.read (PermissionCatalog.DataSourceExtraReadPermission).
/// </summary>
public sealed class ClientDocumentDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant, ITenantClock? clock = null) : IDataSource
{
    public string Key => ClientDocumentCodes.EntityType;
    public string LabelEs => "Documentos de clientes";
    public string LabelEn => "Client documents";
    public string? EntityTypeCode => null;
    public string? DateField => null;
    public string IdField => "DocumentId";
    public string DefaultBusinessModule => BusinessModules.Operations;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("DocumentId", "Id del documento", "Document id", DataFieldType.Number),
        new DataField("ClientId", "Id del cliente", "Client id", DataFieldType.Number),
        new DataField("ClientCode", "Código del cliente", "Client code", DataFieldType.Text),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("DocumentType", "Código del tipo", "Type code", DataFieldType.Text),
        new DataField("DocumentTypeLabel", "Tipo de documento", "Document type", DataFieldType.Text),
        new DataField("DocumentNumber", "Número", "Number", DataFieldType.Text),
        new DataField("ValidFrom", "Vigente desde", "Valid from", DataFieldType.Date),
        new DataField("ValidTo", "Vence el", "Expires on", DataFieldType.Date),
        new DataField("DaysToExpiry", "Días para vencer", "Days to expiry", DataFieldType.Number),
        new DataField("ExpiryState", "Estado de vencimiento", "Expiry state", DataFieldType.Text),
        new DataField("IsTax", "De impuestos", "Tax document", DataFieldType.Bool),
        new DataField("BacksExemption", "Respalda exención de IVU", "Backs a sales tax exemption", DataFieldType.Bool),
        new DataField("CreatedAtUtc", "Subido", "Uploaded", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Client", EntityTypes.Client, "ClientId", "Cliente", "Client"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        if (tenant.TenantId is not int tenantId) return new List<DataRow>();
        if (q.Ids is { Count: 0 }) return new List<DataRow>();
        var today = ClientDataSourceHelpers.Today(clock);
        var csv = await db.Tenants.AsNoTracking().Where(t => t.TenantId == tenantId).Select(t => t.DocExpiryThresholdDays).FirstOrDefaultAsync(ct);
        var warnDays = ExpiryNoticeRules.WarnDays(ExpiryNoticeRules.ThresholdsOrDefault(csv));

        var rows = await ClientDocumentQueries.ValidityRowsAsync(db, lookups, tenantId, new ValidityFilter { DocumentIds = q.Ids, IncludeMissing = true }, ct);
        var result = new List<DataRow>(Math.Min(rows.Count, ClientDataSourceHelpers.MaxRows));
        foreach (var r in rows.Take(ClientDataSourceHelpers.MaxRows))
        {
            result.Add(new DataRow
            {
                ["DocumentId"] = r.DocumentId,
                ["ClientId"] = r.ClientId,
                ["ClientCode"] = r.ClientCode,
                ["ClientName"] = r.ClientName,
                ["DocumentType"] = r.TypeCode,
                ["DocumentTypeLabel"] = MultilingualText.Resolve(r.TypeLabelJson, tenant.Lang),
                ["DocumentNumber"] = r.Number,
                ["ValidFrom"] = r.ValidFrom,
                ["ValidTo"] = r.ValidTo,
                ["DaysToExpiry"] = ClientDocumentRules.DaysToExpiry(r.ValidTo, today),
                ["ExpiryState"] = ClientDocumentRules.ExpiryState(r.ValidTo, today, warnDays, r.IsRenewed, r.Marks.RequiresValidTo),
                ["IsTax"] = r.Marks.IsTax,
                ["BacksExemption"] = r.BackingExemptionId is not null,
                ["CreatedAtUtc"] = r.CreatedAtUtc,
            });
        }
        return result;
    }
}
