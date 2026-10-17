using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Analytics;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 90 (P12) — fuente KPI_DAILY: el historial de las fotos diarias (KpiDailySnapshot), una fila por día e indicador, para indicadores y gráficos que quieran ver cómo van las cosas en el tiempo («órdenes sin chofer por
/// día», «COD cobrado sin cuadrar por día»). Actividad de período por SnapshotDate (el instante es las 00:00 locales del día). Lectura: analytics.view (DataSourceExtraReadPermission). AsNoTracking bajo el filtro de tenant,
/// tope ClientDataSourceHelpers.MaxRows (los más recientes), respeto de q.Ids y del rango (desde inclusivo, hasta exclusivo). Nombres de campo estables.
/// </summary>
public sealed class KpiDailyDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant, ITenantClock clock) : IDataSource
{
    public string Key => KpiCodes.DataSource;
    public string LabelEs => "Historial diario de indicadores";
    public string LabelEn => "Daily indicator history";
    public string? EntityTypeCode => null;
    public string? DateField => "SnapshotDate";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Operations;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("SnapshotDate", "Día", "Day", DataFieldType.Date),
        new DataField("KpiKey", "Clave del indicador", "Indicator key", DataFieldType.Text),
        new DataField("KpiLabel", "Indicador", "Indicator", DataFieldType.Text),
        new DataField("Kind", "Tipo (cantidad o dinero)", "Kind (count or money)", DataFieldType.Text),
        new DataField("Value", "Valor", "Value", DataFieldType.Number),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Set<KpiSnapshot>().AsNoTracking().Where(s => s.DimEntityLookupId == null && s.DimId == null);
        if (q.Ids is not null)
        {
            var wanted = q.Ids.Select(i => Convert.ToInt64(i)).ToList();
            query = query.Where(s => wanted.Contains(s.KpiDailySnapshotId));
        }
        // el rango llega en instantes UTC (00:00 locales): se acota por día en la base y se afina en memoria
        if (q.FromUtc is DateTime from) { var d = clock.DayOf(from).AddDays(-1); query = query.Where(s => s.SnapshotDate >= d); }
        if (q.ToUtc is DateTime to) { var d = clock.DayOf(to).AddDays(1); query = query.Where(s => s.SnapshotDate <= d); }
        var rows = await query.OrderByDescending(s => s.SnapshotDate).ThenBy(s => s.KpiKeyLookupId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        var lang = tenant.Lang;
        var result = new List<DataRow>(rows.Count);
        var keys = new Dictionary<int, string>();
        foreach (var r in rows)
        {
            var at = clock.StartOfDayUtc(r.SnapshotDate);
            if (q.FromUtc is DateTime f && at < f) continue;
            if (q.ToUtc is DateTime t && at >= t) continue;
            if (!keys.TryGetValue(r.KpiKeyLookupId, out var key)) keys[r.KpiKeyLookupId] = key = (await lookups.GetAsync(r.KpiKeyLookupId, ct))?.InternalCode ?? string.Empty;
            var def = KpiKeys.Find(key);
            result.Add(new DataRow
            {
                ["Id"] = r.KpiDailySnapshotId,
                ["SnapshotDate"] = at,
                ["KpiKey"] = key,
                ["KpiLabel"] = def is null ? key : (string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase) ? def.LabelEn : def.LabelEs),
                ["Kind"] = def?.Kind ?? KpiKeys.KindCount,
                ["Value"] = r.NumericValue ?? 0m,
            });
        }
        return result;
    }
}
