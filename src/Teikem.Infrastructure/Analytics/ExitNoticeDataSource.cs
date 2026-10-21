using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.ExitNotices;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Menores (2026-10-21, autónomo) — fuente EXIT_NOTICE (una fila por aviso de salida) para Vistas, indicadores y gráficos, con el mismo patrón de las órdenes de venta: AsNoTracking bajo el filtro global de
/// tenant, tope <see cref="ClientDataSourceHelpers.MaxRows"/>, respeto de q.Ids y del rango sobre CreatedAtUtc. IsOpen = solicitado o despachado parcial; HasBackorder = alguna línea espera inventario;
/// IsLate = abierto con fecha requerida anterior a hoy (día de la compañía); FillRatePct solo en avisos despachados por completo. Lectura: inventory.view y el módulo de almacén con lotes/series.
/// </summary>
public sealed class ExitNoticeDataSource(TeikemDbContext db, ITenantContext tenant, ITenantClock? clock = null) : IDataSource
{
    public string Key => ExitNoticeCodes.EntityType;
    public string LabelEs => "Avisos de salida";
    public string LabelEn => "Exit notices";
    public string? EntityTypeCode => null;
    public string? DateField => "CreatedAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.WmsLotSerial;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Number", "Número de aviso", "Notice number", DataFieldType.Text),
        new DataField("ClientId", "Id de cliente", "Client id", DataFieldType.Number),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("WarehouseId", "Id de almacén", "Warehouse id", DataFieldType.Number),
        new DataField("WarehouseCode", "Almacén", "Warehouse", DataFieldType.Text),
        new DataField("Reference", "Referencia", "Reference", DataFieldType.Text),
        new DataField("Destination", "Destino", "Destination", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("IsOpen", "Abierto", "Open", DataFieldType.Bool),
        new DataField("HasBackorder", "Con pendiente", "Has backorder", DataFieldType.Bool),
        new DataField("IsLate", "Atrasado", "Late", DataFieldType.Bool),
        new DataField("IsAssigned", "Asignado", "Assigned", DataFieldType.Bool),
        new DataField("RequestedDate", "Fecha del aviso", "Notice date", DataFieldType.Date),
        new DataField("RequiredDate", "Fecha requerida", "Required date", DataFieldType.Date),
        new DataField("LineCount", "Líneas", "Lines", DataFieldType.Number),
        new DataField("UnitsOrdered", "Unidades pedidas", "Units ordered", DataFieldType.Number),
        new DataField("UnitsReserved", "Unidades reservadas", "Units reserved", DataFieldType.Number),
        new DataField("UnitsShipped", "Unidades despachadas", "Units shipped", DataFieldType.Number),
        new DataField("UnitsBackordered", "Unidades pendientes", "Units backordered", DataFieldType.Number),
        new DataField("FillRatePct", "Cumplimiento (%)", "Fill rate (%)", DataFieldType.Number),
        new DataField("CreatedAtUtc", "Creado el", "Created at", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Client", EntityTypes.Client, "ClientId", "Cliente", "Client"),
        new DataRelation("Warehouse", EntityTypes.Warehouse, "WarehouseId", "Almacén", "Warehouse"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Set<ExitNotice>().AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(n => wanted.Contains(n.ExitNoticeId));
        }
        if (q.FromUtc is DateTime from) query = query.Where(n => n.CreatedAtUtc >= from);
        if (q.ToUtc is DateTime to) query = query.Where(n => n.CreatedAtUtc < to);
        var notices = await query.OrderByDescending(n => n.ExitNoticeId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (notices.Count == 0) return new List<DataRow>();

        var ids = notices.Select(n => n.ExitNoticeId).ToList();
        var lines = await db.Set<ExitNoticeLine>().AsNoTracking().Where(l => ids.Contains(l.ExitNoticeId))
            .GroupBy(l => l.ExitNoticeId)
            .Select(g => new
            {
                NoticeId = g.Key,
                Count = g.Count(),
                Ordered = g.Sum(l => l.QtyOrdered),
                Reserved = g.Sum(l => l.QtyReserved),
                Shipped = g.Sum(l => l.QtyShipped),
                Backordered = g.Where(l => !l.IsClosed).Sum(l => l.QtyBackordered),
            })
            .ToDictionaryAsync(x => x.NoticeId, ct);
        var clients = await SalesDataSourceHelpers.ClientNamesAsync(db, notices.Select(n => n.ClientId).Distinct().ToList(), ct);
        var warehouses = await SalesDataSourceHelpers.WarehouseCodesAsync(db, notices.Select(n => n.WarehouseId).Distinct().ToList(), ct);
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, ExitNoticeCodes.StatusDomain, ct);
        var today = ClientDataSourceHelpers.Today(clock);
        var lang = tenant.Lang;

        var rows = new List<DataRow>(notices.Count);
        foreach (var n in notices)
        {
            var code = ClientDataSourceHelpers.StatusCodeOf(statusMap, n.StatusCodeId);
            var l = lines.GetValueOrDefault(n.ExitNoticeId);
            var done = code == ExitNoticeStatuses.Shipped;
            rows.Add(new DataRow
            {
                ["Id"] = n.ExitNoticeId,
                ["PublicId"] = n.PublicId.ToString(),
                ["Number"] = n.Number,
                ["ClientId"] = n.ClientId,
                ["ClientName"] = clients.GetValueOrDefault(n.ClientId),
                ["WarehouseId"] = n.WarehouseId,
                ["WarehouseCode"] = warehouses.GetValueOrDefault(n.WarehouseId),
                ["Reference"] = n.Reference,
                ["Destination"] = n.Destination,
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, n.StatusCodeId, lang),
                ["StatusCode"] = code,
                ["IsOpen"] = SalesAnalyticsRules.IsOpen(code),
                ["HasBackorder"] = (l?.Backordered ?? 0m) > 0m,
                ["IsLate"] = SalesAnalyticsRules.IsLate(code, n.RequiredDate, today),
                ["IsAssigned"] = n.AssignedToUserId != null,
                ["RequestedDate"] = n.RequestedDate,
                ["RequiredDate"] = n.RequiredDate,
                ["LineCount"] = l?.Count ?? 0,
                ["UnitsOrdered"] = l?.Ordered ?? 0m,
                ["UnitsReserved"] = l?.Reserved ?? 0m,
                ["UnitsShipped"] = l?.Shipped ?? 0m,
                ["UnitsBackordered"] = l?.Backordered ?? 0m,
                ["FillRatePct"] = done ? SalesAnalyticsRules.FillRatePct(l?.Ordered ?? 0m, l?.Shipped ?? 0m) : null,
                ["CreatedAtUtc"] = n.CreatedAtUtc,
            });
        }
        return rows;
    }
}
