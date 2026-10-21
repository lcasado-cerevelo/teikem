using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Analytics;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Lote 90 (P12) — lectura del historial de las fotos diarias (la usan los ríos del Pulso para dibujar sus barritas de 7 días).</summary>
public sealed class KpiHistory(TeikemDbContext db, ILookupCache lookups, ITenantClock clock)
{
    /// <summary>El valor de un indicador en cada uno de los últimos `days` días (el último es hoy), 0 donde no hay foto.</summary>
    public async Task<IReadOnlyList<decimal>> SeriesAsync(string key, int days, CancellationToken ct)
    {
        var window = KpiRules.Window(clock.Today, days);
        var keyId = await lookups.TryGetIdAsync(KpiCodes.KeyDomain, key, ct);
        if (keyId is null) return KpiRules.Series(window, new Dictionary<DateOnly, decimal>());
        var first = window[0];
        var rows = await db.Set<KpiSnapshot>().AsNoTracking().Where(s => s.KpiKeyLookupId == keyId && s.DimEntityLookupId == null && s.DimId == null && s.SnapshotDate >= first).Select(s => new { s.SnapshotDate, s.NumericValue }).ToListAsync(ct);
        return KpiRules.Series(window, rows.ToDictionary(r => r.SnapshotDate, r => r.NumericValue ?? 0m));
    }
}

/// <summary>
/// Lote 90 (P12) — la foto diaria: guarda en KpiDailySnapshot las cifras de HOY (día de la compañía) de los ríos y del Radar para que haya historial. La toma el trabajo `KPI_SNAPSHOT` cada noche; «Ejecutar
/// ahora» la repite y SOBRE-ESCRIBE la de hoy (una sola fila por día, indicador y dimensión), así que se puede correr varias veces. Solo guarda los indicadores de los módulos encendidos (órdenes: LTL_GROUND; COD: COD).
/// </summary>
public sealed class KpiSnapshotService(TeikemDbContext db, ITenantContext tenant, ITenantClock clock, ILookupCache lookups, ModuleService modules, PulseRiverService rivers, PulseRadarService radar)
{
    private TenantContext T => (TenantContext)tenant;

    /// <summary>Calcula las cifras de ahora sin guardar nada (lo que la foto guardaría).</summary>
    public async Task<IReadOnlyDictionary<string, decimal>> ComputeAsync(CancellationToken ct)
    {
        var values = new Dictionary<string, decimal>();
        if (await modules.IsEnabledAsync(KpiKeys.ModuleOrders, ct))
        {
            var orders = (await rivers.OrdersAsync(ct)).Nodes.ToDictionary(n => n.Key, n => n.Value);
            values[KpiKeys.OrdersCreated] = orders["ENTRY"];
            var stock = await rivers.StockAsync(ct);   // las existencias de ahora; el río ya no las muestra (muestra lo que entró al almacén hoy)
            values[KpiKeys.OrdersInWarehouse] = stock.InWarehouse;
            values[KpiKeys.OrdersUnassigned] = stock.ToDispatch;
            values[KpiKeys.OrdersOnRoute] = orders["ON_ROUTE"];
            values[KpiKeys.OrdersDelivered] = orders["DELIVERED"];
            values[KpiKeys.OrdersOpen] = (await radar.RadarAsync(null, 1, ct)).OpenCount;
        }
        if (await modules.IsEnabledAsync(KpiKeys.ModuleCod, ct))
        {
            var cod = (await rivers.CodAsync(ct)).Nodes.ToDictionary(n => n.Key, n => n);
            values[KpiKeys.CodToCollect] = cod["TO_COLLECT"].Details.Single(d => d.Key == "amount").Value;
            values[KpiKeys.CodCollected] = cod["COLLECTED"].Value;
            values[KpiKeys.CodReconciled] = cod["RECONCILED"].Value;
            values[KpiKeys.CodRemittanceReady] = cod["REMITTANCE"].Value;
        }
        return values;
    }

    /// <summary>Guarda la foto de hoy (alta o reemplazo por indicador). Devuelve el día y cuántas cifras guardó.</summary>
    public async Task<(DateOnly Day, int Saved)> CaptureAsync(CancellationToken ct)
    {
        var day = clock.Today;
        var values = await ComputeAsync(ct);
        if (values.Count == 0) return (day, 0);
        var tenantId = T.RequireTenantId();
        var ids = new Dictionary<string, int>();
        foreach (var key in values.Keys) ids[key] = await lookups.GetIdAsync(KpiCodes.KeyDomain, key, ct);
        var idList = ids.Values.ToList();
        var existing = await db.Set<KpiSnapshot>().AsTracking().Where(s => s.SnapshotDate == day && s.DimEntityLookupId == null && s.DimId == null && idList.Contains(s.KpiKeyLookupId)).ToListAsync(ct);
        foreach (var (key, value) in values)
        {
            var row = existing.FirstOrDefault(s => s.KpiKeyLookupId == ids[key]);
            if (row is null) db.Set<KpiSnapshot>().Add(new KpiSnapshot { TenantId = tenantId, SnapshotDate = day, KpiKeyLookupId = ids[key], NumericValue = value });
            else row.NumericValue = value;
        }
        await db.SaveChangesAsync(ct);
        return (day, values.Count);
    }

    /// <summary>Las cifras guardadas de un indicador (del más viejo al más nuevo) para los últimos `days` días, con su fecha.</summary>
    public async Task<IReadOnlyList<(DateOnly Day, decimal Value)>> HistoryAsync(string key, int days, CancellationToken ct)
    {
        var window = KpiRules.Window(clock.Today, days);
        var keyId = await lookups.TryGetIdAsync(KpiCodes.KeyDomain, key, ct);
        var byDay = keyId is null ? new Dictionary<DateOnly, decimal>()
            : (await db.Set<KpiSnapshot>().AsNoTracking().Where(s => s.KpiKeyLookupId == keyId && s.DimEntityLookupId == null && s.DimId == null && s.SnapshotDate >= window[0]).ToListAsync(ct)).ToDictionary(s => s.SnapshotDate, s => s.NumericValue ?? 0m);
        return window.Select(d => (d, byDay.GetValueOrDefault(d))).ToList();
    }
}
