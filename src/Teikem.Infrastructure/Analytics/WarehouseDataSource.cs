using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 6 (P1) — fuente de datos WAREHOUSE (almacenes del tenant; estado actual, sin rango de fecha) para vistas,
/// indicadores y gráficos. Sigue el patrón de ClientDataSource: lectura AsNoTracking bajo el filtro de tenant, tope de
/// ClientDataSourceHelpers.MaxRows filas y respeto de q.Ids (destino de relaciones muchos-a-uno).
/// - ZoneCount y DockCount cuentan los ACTIVOS; BinCount cuenta todas las posiciones y ActiveBinCount las activas.
/// - QtyOnHand = Σ StockBalance.QtyOnHand del almacén.
/// - Conteos y existencias en consultas agrupadas (sin N+1). Las hijas (zona, posición, muelle) se alcanzan por los ids
///   de almacenes ya filtrados.
/// - Nombres de campo estables: SystemAnalyticsSeeder y AnalyticsSeedFieldsTests los usan.
/// - Lote 16: ReceivingModeCode (PUTAWAY | DIRECT; NULL en la base = PUTAWAY) y su etiqueta ReceivingMode (catálogo
///   'ReceivingMode', en una consulta).
/// </summary>
public sealed class WarehouseDataSource(TeikemDbContext db, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.Warehouse;
    public string LabelEs => "Almacenes";
    public string LabelEn => "Warehouses";
    public string? EntityTypeCode => EntityTypes.Warehouse;
    public string? DateField => null;
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Code", "Código", "Code", DataFieldType.Text),
        new DataField("Name", "Nombre", "Name", DataFieldType.Text),
        new DataField("City", "Pueblo", "City", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("IsActive", "Activo", "Active", DataFieldType.Bool),
        new DataField("ZoneCount", "Zonas", "Zones", DataFieldType.Number),
        new DataField("BinCount", "Posiciones", "Bins", DataFieldType.Number),
        new DataField("ActiveBinCount", "Posiciones activas", "Active bins", DataFieldType.Number),
        new DataField("DockCount", "Muelles", "Docks", DataFieldType.Number),
        new DataField("QtyOnHand", "Existencia en mano", "Quantity on hand", DataFieldType.Number),
        new DataField("ReceivingMode", "Modo de recepción", "Receiving mode", DataFieldType.Text),              // Lote 16
        new DataField("ReceivingModeCode", "Código de modo de recepción", "Receiving mode code", DataFieldType.Text),   // Lote 16
    };

    public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Warehouses.AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(w => wanted.Contains(w.WarehouseId));
        }
        var warehouses = await query.OrderBy(w => w.Code).ThenBy(w => w.WarehouseId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (warehouses.Count == 0) return new List<DataRow>();

        var ids = warehouses.Select(w => w.WarehouseId).ToList();
        var zones = await db.WarehouseZones.AsNoTracking().Where(z => ids.Contains(z.WarehouseId) && z.IsActive)
            .GroupBy(z => z.WarehouseId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
        var bins = await db.WarehouseBins.AsNoTracking().Where(b => ids.Contains(b.WarehouseId))
            .GroupBy(b => b.WarehouseId).Select(g => new { g.Key, All = g.Count(), Active = g.Count(b => b.IsActive) })
            .ToDictionaryAsync(x => x.Key, ct);
        var docks = await db.WarehouseDocks.AsNoTracking().Where(d => ids.Contains(d.WarehouseId) && d.IsActive)
            .GroupBy(d => d.WarehouseId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
        var onHand = await db.StockBalances.AsNoTracking().Where(s => ids.Contains(s.WarehouseId))
            .GroupBy(s => s.WarehouseId).Select(g => new { g.Key, Q = g.Sum(s => s.QtyOnHand) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.WarehouseStatus, ct);
        var lang = tenant.Lang;
        var modes = await ReceivingModeLabelsAsync(db, lang, ct);

        var rows = new List<DataRow>(warehouses.Count);
        foreach (var w in warehouses)
        {
            var b = bins.GetValueOrDefault(w.WarehouseId);
            rows.Add(new DataRow
            {
                ["Id"] = w.WarehouseId,
                ["PublicId"] = w.PublicId.ToString(),
                ["Code"] = w.Code,
                ["Name"] = w.Name,
                ["City"] = w.City,
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, w.StatusCodeId, lang),
                ["StatusCode"] = ClientDataSourceHelpers.StatusCodeOf(statusMap, w.StatusCodeId),
                ["IsActive"] = w.IsActive,
                ["ZoneCount"] = zones.GetValueOrDefault(w.WarehouseId),
                ["BinCount"] = b?.All ?? 0,
                ["ActiveBinCount"] = b?.Active ?? 0,
                ["DockCount"] = docks.GetValueOrDefault(w.WarehouseId),
                ["QtyOnHand"] = onHand.GetValueOrDefault(w.WarehouseId),
                ["ReceivingMode"] = ReceivingModeLabel(modes, w.ReceivingModeLookupId),
                ["ReceivingModeCode"] = ReceivingModeCode(modes, w.ReceivingModeLookupId),
            });
        }
        return rows;
    }

    // ---------------------------------------------------------------- Lote 16: modo de recepción (compartido con ReceiptDataSource)

    /// <summary>Catálogo 'ReceivingMode' (código y etiqueta en el idioma del usuario) por LookupCodeId, en una consulta.</summary>
    internal static async Task<Dictionary<int, (string Code, string Label)>> ReceivingModeLabelsAsync(TeikemDbContext db, string? lang, CancellationToken ct)
        => (await db.LookupCodes.AsNoTracking().Where(l => l.Entity == LookupDomains.ReceivingMode)
                .Select(l => new { l.LookupCodeId, l.InternalCode, l.LabelJson }).ToListAsync(ct))
            .ToDictionary(l => l.LookupCodeId, l => (l.InternalCode, MultilingualText.Resolve(l.LabelJson, lang)));

    /// <summary>Código del modo (NULL o desconocido = PUTAWAY).</summary>
    internal static string ReceivingModeCode(IReadOnlyDictionary<int, (string Code, string Label)> modes, int? lookupId)
        => ReceivingModeRules.Normalize(lookupId is int id && modes.TryGetValue(id, out var m) ? m.Code : null);

    /// <summary>Etiqueta del modo (la de PUTAWAY si es NULL; el código si el catálogo no lo tiene).</summary>
    internal static string ReceivingModeLabel(IReadOnlyDictionary<int, (string Code, string Label)> modes, int? lookupId)
    {
        var code = ReceivingModeCode(modes, lookupId);
        return modes.Values.Where(m => m.Code == code).Select(m => m.Label).FirstOrDefault() ?? code;
    }
}
