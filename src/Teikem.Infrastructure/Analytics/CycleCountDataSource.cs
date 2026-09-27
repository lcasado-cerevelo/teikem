using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 7A (P2) — fuente de datos CYCLE_COUNT (conteos cíclicos activos del tenant) para vistas, indicadores y gráficos
/// (indicador de Pulso 'Conteos con diferencia'). Sigue el patrón de WarehouseDataSource y ReceiptDataSource: AsNoTracking bajo
/// el filtro de tenant (CycleCount es ITenantScoped), tope ClientDataSourceHelpers.MaxRows, respeto de q.Ids y del rango.
/// - Actividad de período por ReconciledAtUtc (fecha de reconciliación; los conteos abiertos o terminados sin reconciliar no
///   tienen fecha y quedan fuera de un rango), igual que RECEIPT con ReceivedAtUtc.
/// - Las líneas (sin TenantId) se alcanzan SOLO por los ids de conteos ya filtrados, agregadas en SQL (sin N+1).
/// - Diferencia por línea = CONTADO − base: la base es el saldo bloqueado al reconciliar (ReconciledSystemQty, lo que se
///   asentó en el ledger, D22) y, antes de reconciliar, la foto (SystemQty). Nunca VarianceQty (columna computada contra la foto).
///   VarianceLines cuenta las líneas contadas con diferencia o con ajuste enlazado (en serie una sustitución ajusta aunque la
///   cantidad cuadre); HasVariance = VarianceLines &gt; 0; NetVariance = Σ diferencias (con signo: pueden compensarse).
/// - Nombres de campo estables: los usa SystemAnalyticsSeeder (AnalyticsSeedFieldsTests lo verifica).
/// </summary>
public sealed class CycleCountDataSource(TeikemDbContext db, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.CycleCount;
    public string LabelEs => "Conteos cíclicos";
    public string LabelEn => "Cycle counts";
    /// <summary>Sin campos personalizados en el motor de análisis.</summary>
    public string? EntityTypeCode => null;
    /// <summary>Actividad de período por fecha de reconciliación.</summary>
    public string? DateField => "ReconciledAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("Number", "Número de conteo", "Count number", DataFieldType.Text),
        new DataField("WarehouseId", "Id de almacén", "Warehouse id", DataFieldType.Number),
        new DataField("WarehouseCode", "Almacén", "Warehouse", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("LineCount", "Líneas", "Lines", DataFieldType.Number),
        new DataField("CountedLines", "Líneas contadas", "Counted lines", DataFieldType.Number),
        new DataField("VarianceLines", "Líneas con diferencia", "Lines with variance", DataFieldType.Number),
        new DataField("NetVariance", "Diferencia neta", "Net variance", DataFieldType.Number),
        new DataField("HasVariance", "Con diferencia", "Has variance", DataFieldType.Bool),
        new DataField("CreatedAtUtc", "Creado el", "Created at", DataFieldType.Date),
        new DataField("ReconciledAtUtc", "Reconciliado el", "Reconciled at", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Warehouse", EntityTypes.Warehouse, "WarehouseId", "Almacén", "Warehouse"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.CycleCounts.AsNoTracking().Where(c => c.IsActive);
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(c => wanted.Contains(c.CycleCountId));
        }
        if (q.FromUtc is DateTime from) query = query.Where(c => c.ReconciledAtUtc != null && c.ReconciledAtUtc >= from);
        if (q.ToUtc is DateTime to) query = query.Where(c => c.ReconciledAtUtc != null && c.ReconciledAtUtc < to);

        var counts = await query.OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.CycleCountId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (counts.Count == 0) return new List<DataRow>();

        var ids = counts.Select(c => c.CycleCountId).ToList();
        var whIds = counts.Select(c => c.WarehouseId).Distinct().ToList();
        var summaries = await db.CycleCountLines.AsNoTracking()
            .Where(l => ids.Contains(l.CycleCountId))
            .GroupBy(l => l.CycleCountId)
            .Select(g => new
            {
                CycleCountId = g.Key,
                Lines = g.Count(),
                Counted = g.Count(l => l.CountedQty != null),
                WithVariance = g.Count(l => l.CountedQty != null
                    && (l.CountedQty != (l.ReconciledSystemQty ?? l.SystemQty) || l.AdjustmentTxnId != null)),
                Net = g.Sum(l => l.CountedQty != null ? l.CountedQty.Value - (l.ReconciledSystemQty ?? l.SystemQty) : 0m),
            })
            .ToDictionaryAsync(x => x.CycleCountId, ct);
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => whIds.Contains(w.WarehouseId))
            .ToDictionaryAsync(w => w.WarehouseId, w => w.Code, ct);
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.CycleCountStatus, ct);
        var lang = tenant.Lang;

        var rows = new List<DataRow>(counts.Count);
        foreach (var c in counts)
        {
            var s = summaries.GetValueOrDefault(c.CycleCountId);
            var varianceLines = s?.WithVariance ?? 0;
            rows.Add(new DataRow
            {
                ["Id"] = c.CycleCountId,
                ["Number"] = c.Number,
                ["WarehouseId"] = c.WarehouseId,
                ["WarehouseCode"] = warehouses.GetValueOrDefault(c.WarehouseId),
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, c.StatusCodeId, lang),
                ["StatusCode"] = ClientDataSourceHelpers.StatusCodeOf(statusMap, c.StatusCodeId),
                ["LineCount"] = s?.Lines ?? 0,
                ["CountedLines"] = s?.Counted ?? 0,
                ["VarianceLines"] = varianceLines,
                ["NetVariance"] = s?.Net ?? 0m,
                ["HasVariance"] = varianceLines > 0,
                ["CreatedAtUtc"] = c.CreatedAtUtc,
                ["ReconciledAtUtc"] = c.ReconciledAtUtc,
            });
        }
        return rows;
    }
}
