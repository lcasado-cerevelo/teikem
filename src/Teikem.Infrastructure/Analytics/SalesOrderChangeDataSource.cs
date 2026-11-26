using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 249 (Lote E) — fuente SALES_ORDER_CHANGE: una fila por modificación de orden de venta (o adición) para vistas, indicadores y gráficos. Actividad de período
/// por RegisteredAtUtc. Lectura: sales.view (PermissionCatalog.DataSourceExtraReadPermission) y el módulo Órdenes de venta encendido. Relación muchos-a-uno con la
/// orden (SALES_ORDER por OrderId). AsNoTracking bajo el filtro global de compañía (SalesOrderChange es ITenantScoped), tope ClientDataSourceHelpers.MaxRows,
/// respeta q.Ids y el rango (desde inclusivo, hasta exclusivo). Nombres de campo estables.
/// </summary>
public sealed class SalesOrderChangeDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant) : IDataSource
{
    public string Key => SalesChangeCodes.EntityType;
    public string LabelEs => "Modificaciones de órdenes de venta";
    public string LabelEn => "Sales order changes";
    public string? EntityTypeCode => null;
    public string? DateField => "RegisteredAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.SalesOrders;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("OrderId", "Id de la orden", "Order id", DataFieldType.Number),
        new DataField("OrderNumber", "Orden", "Order", DataFieldType.Text),
        new DataField("ChangeNo", "N.º de modificación", "Change no.", DataFieldType.Number),
        new DataField("Kind", "Tipo", "Kind", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("RegisteredByName", "Registró", "Registered by", DataFieldType.Text),
        new DataField("AuthorizedByName", "Autorizó (cliente)", "Authorized by (customer)", DataFieldType.Text),
        new DataField("AuthorizedByTitle", "Cargo de quien autorizó", "Authorizer title", DataFieldType.Text),
        new DataField("Channel", "Canal de autorización", "Authorization channel", DataFieldType.Text),
        new DataField("AuthorizedAtUtc", "Autorizada el", "Authorized at", DataFieldType.Date),
        new DataField("Reason", "Motivo", "Reason", DataFieldType.Text),
        new DataField("TotalBefore", "Total antes", "Total before", DataFieldType.Number, IsMoney: true),
        new DataField("TotalAfter", "Total después", "Total after", DataFieldType.Number, IsMoney: true),
        new DataField("TotalDelta", "Diferencia", "Difference", DataFieldType.Number, IsMoney: true),
        new DataField("LinesAdded", "Líneas agregadas", "Lines added", DataFieldType.Number),
        new DataField("LinesRemoved", "Líneas quitadas", "Lines removed", DataFieldType.Number),
        new DataField("LinesChanged", "Líneas cambiadas", "Lines changed", DataFieldType.Number),
        new DataField("HasPriceChange", "Cambió precio", "Price changed", DataFieldType.Bool),
        new DataField("HasTaxOverride", "IVU manual", "Manual tax", DataFieldType.Bool),
        new DataField("HasTermsChange", "Cambió términos", "Terms changed", DataFieldType.Bool),
        new DataField("ReopenedPack", "Reabrió empaque", "Reopened pack", DataFieldType.Bool),
        new DataField("ReversedBatchCount", "Recolecciones deshechas", "Picks undone", DataFieldType.Number),
        new DataField("CreditOverride", "Crédito autorizado", "Credit override", DataFieldType.Bool),
        new DataField("RequiresApproval", "Pidió segunda aprobación", "Required second approval", DataFieldType.Bool),
        new DataField("DecidedByName", "Decidió", "Decided by", DataFieldType.Text),
        new DataField("DecidedAtUtc", "Decidida el", "Decided at", DataFieldType.Date),
        new DataField("AdditionOrderNumber", "Orden de adición", "Addition order", DataFieldType.Text),
        new DataField("RegisteredAtUtc", "Registrada el", "Registered at", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Order", SalesCodes.EntityType, "OrderId", "Orden de venta", "Sales order"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Set<SalesOrderChange>().AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(c => wanted.Contains(c.SalesOrderChangeId));
        }
        if (q.FromUtc is DateTime from) query = query.Where(c => c.RegisteredAtUtc >= from);
        if (q.ToUtc is DateTime to) query = query.Where(c => c.RegisteredAtUtc < to);
        var rows = await query.OrderByDescending(c => c.SalesOrderChangeId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (rows.Count == 0) return new List<DataRow>();

        var orderIds = rows.Select(r => r.SalesOrderId).Concat(rows.Where(r => r.AdditionSalesOrderId != null).Select(r => r.AdditionSalesOrderId!.Value)).Distinct().ToList();
        var orders = await db.Set<SalesOrder>().AsNoTracking().Where(o => orderIds.Contains(o.SalesOrderId))
            .Select(o => new { o.SalesOrderId, o.Number, o.ClientId }).ToDictionaryAsync(o => o.SalesOrderId, ct);
        var clients = await SalesDataSourceHelpers.ClientNamesAsync(db, orders.Values.Select(o => o.ClientId).Distinct().ToList(), ct);
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, SalesChangeCodes.StatusDomain, ct);
        var names = await Services.AssigneeCheck.NamesAsync(db, rows.Select(r => r.RegisteredBy).Concat(rows.Where(r => r.DecidedBy != null).Select(r => r.DecidedBy!.Value)).Distinct(), ct);
        var lang = tenant.Lang;
        var channels = new Dictionary<int, string?>();
        foreach (var id in rows.Select(r => r.AuthorizationChannelLookupId).Distinct())
        {
            var l = await lookups.GetAsync(id, ct);
            channels[id] = l is null ? null : MultilingualText.Resolve(l.LabelJson, lang);
        }

        var result = new List<DataRow>(rows.Count);
        foreach (var c in rows)
        {
            var o = orders.GetValueOrDefault(c.SalesOrderId);
            var add = c.AdditionSalesOrderId is int aid ? orders.GetValueOrDefault(aid) : null;
            result.Add(new DataRow
            {
                ["Id"] = c.SalesOrderChangeId,
                ["PublicId"] = c.PublicId.ToString(),
                ["OrderId"] = c.SalesOrderId,
                ["OrderNumber"] = o?.Number,
                ["ChangeNo"] = c.ChangeNo,
                ["Kind"] = c.Kind,
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, c.StatusCodeId, lang),
                ["StatusCode"] = ClientDataSourceHelpers.StatusCodeOf(statusMap, c.StatusCodeId),
                ["ClientName"] = o is null ? null : clients.GetValueOrDefault(o.ClientId),
                ["RegisteredByName"] = names.GetValueOrDefault(c.RegisteredBy),
                ["AuthorizedByName"] = c.AuthorizedByName,
                ["AuthorizedByTitle"] = c.AuthorizedByTitle,
                ["Channel"] = channels.GetValueOrDefault(c.AuthorizationChannelLookupId),
                ["AuthorizedAtUtc"] = c.AuthorizedAtUtc,
                ["Reason"] = c.Reason,
                ["TotalBefore"] = c.TotalBefore,
                ["TotalAfter"] = c.TotalAfter,
                ["TotalDelta"] = c.TotalDelta,
                ["LinesAdded"] = c.LinesAdded,
                ["LinesRemoved"] = c.LinesRemoved,
                ["LinesChanged"] = c.LinesChanged,
                ["HasPriceChange"] = c.HasPriceChange,
                ["HasTaxOverride"] = c.HasTaxOverride,
                ["HasTermsChange"] = c.HasTermsChange,
                ["ReopenedPack"] = c.ReopenedPack,
                ["ReversedBatchCount"] = c.ReversedBatchCount,
                ["CreditOverride"] = c.CreditOverride,
                ["RequiresApproval"] = c.RequiresApproval,
                ["DecidedByName"] = c.DecidedBy is int d ? names.GetValueOrDefault(d) : null,
                ["DecidedAtUtc"] = c.DecidedAtUtc,
                ["AdditionOrderNumber"] = add?.Number,
                ["RegisteredAtUtc"] = c.RegisteredAtUtc,
            });
        }
        return result;
    }
}
