using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Cuántos avisos nuevos salieron por evento en una revisión (0 = nada nuevo que avisar o ya se había avisado esta semana).</summary>
public sealed record FleetAlertScanResult(int DocsExpiring, int DocsExpired, int MaintenanceDueSoon, int MaintenanceOverdue, int WorkOrdersStale = 0, int FuelOutOfRange = 0, int VehiclesIdle = 0, int DriversIdle = 0)
{
    public int Total => DocsExpiring + DocsExpired + MaintenanceDueSoon + MaintenanceOverdue + WorkOrdersStale + FuelOutOfRange + VehiclesIdle + DriversIdle;
}

/// <summary>
/// Lote 168 (2026-10-21) — revisión de la flota que publica los avisos de <see cref="FleetEventProvider"/>: documentos de vehículos y choferes (registro, seguro, licencia,
/// certificaciones…) por vencer y vencidos, y mantenimiento preventivo próximo y vencido. Lo corre el trabajo diario FLEET_ALERT_SCAN, una compañía a la vez. NO reescribe las
/// consultas: lee con <see cref="FleetDocumentService.GetExpiringAsync"/> (dueños activos y no terminales, solo el documento vigente por tipo) y con
/// <see cref="MaintenanceScheduleService.GetDueAsync"/> (el mismo cálculo del panel «Mantenimiento preventivo»). Cada evento sale como UN aviso con el conjunto completo; la clave
/// de no repetición (<see cref="InventoryAlertRules.DedupKey"/>) evita repetirlo si el conjunto no cambió esa semana.
/// </summary>
public sealed class FleetAlertService(TeikemDbContext db, ITenantClock clock, INotificationPublisher notifications, FleetDocumentService documents, MaintenanceScheduleService maintenance)
{
    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Num(decimal n) => Math.Abs(n).ToString("0.#", CultureInfo.InvariantCulture);

    private sealed record Detected(string Id, string Text);

    public async Task<FleetAlertScanResult> ScanAsync(CancellationToken ct)
    {
        var warnDays = await db.Tenants.AsNoTracking().Where(t => t.TenantId == db.CurrentTenantId)
            .Select(t => (int?)t.FleetDocWarnDays).FirstOrDefaultAsync(ct) ?? FleetAlertRules.DefaultDocWarnDays;

        var today = clock.Today;
        var docs = await documents.GetExpiringAsync(new ExpiringDocumentsQuery(WithinDays: warnDays, IncludeExpired: true), ct);
        var expiring = new List<Detected>();
        var expired = new List<Detected>();
        foreach (var d in docs.OrderBy(d => d.ExpiryDate).ThenBy(d => d.OwnerCode, StringComparer.OrdinalIgnoreCase))
        {
            var who = (d.OwnerKind == FleetOwnerKinds.Vehicle ? "V " : "C ") + d.OwnerCode;
            if (FleetAlertRules.IsExpired(d.ExpiryDate, today))
                expired.Add(new Detected(d.RowKey, $"{who} · {d.DocumentType} · {Day(d.ExpiryDate)} · {-d.DaysToExpiry}"));
            else if (FleetAlertRules.IsExpiring(d.ExpiryDate, today, warnDays))
                expiring.Add(new Detected(d.RowKey, $"{who} · {d.DocumentType} · {Day(d.ExpiryDate)} · {d.DaysToExpiry}"));
        }

        var due = await maintenance.GetDueAsync(new MaintenanceDueQuery(null, new[] { MaintenanceDueStates.DueSoon, MaintenanceDueStates.Overdue }), ct);
        var dueSoon = due.Where(m => m.State == MaintenanceDueStates.DueSoon).Select(ToDetected).ToList();
        var overdue = due.Where(m => m.State == MaintenanceDueStates.Overdue).Select(ToDetected).ToList();

        var result = new FleetAlertScanResult(
            await PublishAsync(FleetEventCodes.DocExpiring, "FDX", expiring, new Dictionary<string, string?> { ["days"] = warnDays.ToString(CultureInfo.InvariantCulture) }, ct),
            await PublishAsync(FleetEventCodes.DocExpired, "FDXD", expired, null, ct),
            await PublishAsync(FleetEventCodes.MaintenanceDueSoon, "FMS", dueSoon, null, ct),
            await PublishAsync(FleetEventCodes.MaintenanceOverdue, "FMO", overdue, null, ct),
            await PublishAsync(FleetEventCodes.WorkOrderStale, "FWO", await StaleWorkOrdersAsync(today, ct), null, ct),
            await PublishAsync(FleetEventCodes.FuelOutOfRange, "FFU", await FuelOutOfRangeAsync(today, ct), null, ct),
            await PublishAsync(FleetEventCodes.VehicleIdle, "FVI", await IdleVehiclesAsync(today, ct), null, ct),
            await PublishAsync(FleetEventCodes.DriverIdle, "FDI", await IdleDriversAsync(today, ct), null, ct));
        if (result.Total > 0) await db.SaveChangesAsync(ct);
        return result;
    }

    // ---------------------------------------------------------------- Lote 179 (G2)

    /// <summary>Órdenes de trabajo abiertas (OPEN / IN_PROGRESS) por más de <see cref="FleetAlertRules.WorkOrderStaleDays"/> días: «OT-00012 · V-01 · abierta hace 20 días».</summary>
    private async Task<List<Detected>> StaleWorkOrdersAsync(DateOnly today, CancellationToken ct)
    {
        var open = await db.StatusIdsAsync(StatusDomains.WorkOrderStatus, new[] { WorkOrderStatuses.Open, WorkOrderStatuses.InProgress }, ct);
        var rows = await (from w in db.MaintenanceWorkOrders.AsNoTracking()
                          join v in db.Vehicles.AsNoTracking() on w.VehicleId equals v.VehicleId
                          where w.IsActive && open.Contains(w.StatusCodeId)
                          select new { w.WorkOrderId, w.Number, w.CreatedAtUtc, VehicleCode = v.Code }).ToListAsync(ct);
        return rows.Select(r => (r, Created: clock.DayOf(r.CreatedAtUtc)))
            .Where(x => FleetAlertRules.IsWorkOrderStale(x.Created, today))
            .OrderBy(x => x.Created).ThenBy(x => x.r.Number, StringComparer.OrdinalIgnoreCase)
            .Select(x => new Detected(x.r.WorkOrderId.ToString(CultureInfo.InvariantCulture), $"{x.r.Number} · {x.r.VehicleCode} · abierta hace {FleetAlertRules.DaysOpen(x.Created, today)} días")).ToList();
    }

    /// <summary>Vehículos cuya última carga (de los últimos días) rinde fuera de rango contra el promedio de las anteriores: «V-01 · 4.2 km/L · promedio 7.1 · -41 %».</summary>
    private async Task<List<Detected>> FuelOutOfRangeAsync(DateOnly today, CancellationToken ct)
    {
        var since = clock.UtcNow.AddDays(-365);
        var logs = await (from f in db.FuelLogs.AsNoTracking()
                          join v in db.Vehicles.AsNoTracking() on f.VehicleId equals v.VehicleId
                          where f.IsActive && v.IsActive && f.FillDateUtc >= since
                          select new { f.FuelLogId, f.VehicleId, f.FillDateUtc, f.OdometerKm, f.Liters, f.TotalCost, VehicleCode = v.Code }).ToListAsync(ct);
        var result = new List<Detected>();
        foreach (var g in logs.GroupBy(l => l.VehicleId).OrderBy(g => g.First().VehicleCode, StringComparer.OrdinalIgnoreCase))
        {
            var rows = FuelEfficiency.Compute(g.Select(l => new FuelReading(l.FuelLogId, l.FillDateUtc, l.OdometerKm, l.Liters, l.TotalCost))).Rows
                .Where(r => r.KmPerLiter is not null).ToList();
            if (rows.Count == 0) continue;
            var latest = rows[^1];
            if (clock.DayOf(latest.FillDateUtc).DayNumber < today.DayNumber - FleetAlertRules.FuelWindowDays) continue;
            var dev = FleetAlertRules.FuelDeviation(latest.KmPerLiter!.Value, rows.Take(rows.Count - 1).Select(r => r.KmPerLiter!.Value).ToList());
            if (dev is not { Out: true } d) continue;
            result.Add(new Detected($"{g.Key}/{latest.Id}", $"{g.First().VehicleCode} · {Num(latest.KmPerLiter!.Value)} km/L · promedio {Num(d.Mean)} · {(d.DeviationPct > 0 ? "+" : "-")}{Num(d.DeviationPct)} %"));
        }
        return result;
    }

    /// <summary>Vehículos activos sin viaje (no cancelado) en más de <see cref="FleetAlertRules.IdleDays"/> días: «V-03 · sin viaje desde 2026-09-30 · 21 días».</summary>
    private async Task<List<Detected>> IdleVehiclesAsync(DateOnly today, CancellationToken ct)
    {
        var cancelled = await db.StatusIdsAsync(StatusDomains.TripStatus, new[] { TripStatuses.Cancelled }, ct);
        var vehicles = await db.Vehicles.AsNoTracking().Where(v => v.IsActive).Select(v => new { v.VehicleId, v.Code, v.CreatedAtUtc }).ToListAsync(ct);
        var last = (await db.Trips.AsNoTracking().Where(t => t.IsActive && t.VehicleId != null && !cancelled.Contains(t.StatusCodeId))
            .GroupBy(t => t.VehicleId!.Value).Select(g => new { Id = g.Key, Day = g.Max(t => t.PlanDate) }).ToListAsync(ct)).ToDictionary(x => x.Id, x => x.Day);
        return vehicles.OrderBy(v => v.Code, StringComparer.OrdinalIgnoreCase)
            .Select(v => (v, Idle: FleetAlertRules.IdleDaysOf(last.TryGetValue(v.VehicleId, out var d) ? d : null, clock.DayOf(v.CreatedAtUtc), today), Last: last.TryGetValue(v.VehicleId, out var l) ? l : (DateOnly?)null))
            .Where(x => x.Idle is not null)
            .Select(x => new Detected(x.v.VehicleId.ToString(CultureInfo.InvariantCulture), $"{x.v.Code} · {(x.Last is DateOnly ld ? "sin viaje desde " + Day(ld) : "nunca ha tenido viaje")} · {x.Idle} días")).ToList();
    }

    private async Task<List<Detected>> IdleDriversAsync(DateOnly today, CancellationToken ct)
    {
        var cancelled = await db.StatusIdsAsync(StatusDomains.TripStatus, new[] { TripStatuses.Cancelled }, ct);
        var drivers = await db.Drivers.AsNoTracking().Where(d => d.IsActive).Select(d => new { d.DriverId, d.EmployeeCode, d.FullName, d.CreatedAtUtc }).ToListAsync(ct);
        var last = (await db.Trips.AsNoTracking().Where(t => t.IsActive && t.DriverId != null && !cancelled.Contains(t.StatusCodeId))
            .GroupBy(t => t.DriverId!.Value).Select(g => new { Id = g.Key, Day = g.Max(t => t.PlanDate) }).ToListAsync(ct)).ToDictionary(x => x.Id, x => x.Day);
        return drivers.OrderBy(d => d.EmployeeCode, StringComparer.OrdinalIgnoreCase)
            .Select(d => (d, Idle: FleetAlertRules.IdleDaysOf(last.TryGetValue(d.DriverId, out var x) ? x : null, clock.DayOf(d.CreatedAtUtc), today), Last: last.TryGetValue(d.DriverId, out var l) ? l : (DateOnly?)null))
            .Where(x => x.Idle is not null)
            .Select(x => new Detected(x.d.DriverId.ToString(CultureInfo.InvariantCulture), $"{x.d.EmployeeCode} {x.d.FullName} · {(x.Last is DateOnly ld ? "sin viaje desde " + Day(ld) : "nunca ha tenido viaje")} · {x.Idle} días")).ToList();
    }

    /// <summary>«V-01 · Cambio de aceite · faltan 450 km · faltan 12 días» (o «pasado por …» si ya se pasó): solo las dimensiones que el programa usa.</summary>
    private static Detected ToDetected(MaintenanceDueDto m)
    {
        var parts = new List<string> { m.VehicleCode, m.ScheduleName };
        if (m.KmRemaining is decimal km) parts.Add(km > 0 ? $"faltan {Num(km)} km" : $"pasado por {Num(km)} km");
        if (m.DaysRemaining is int days) parts.Add(days > 0 ? $"faltan {days} días" : $"pasado por {-days} días");
        return new Detected($"{m.ScheduleId}/{m.VehiclePublicId}", string.Join(" · ", parts));
    }

    /// <summary>Publica un aviso con el conjunto (si hay algo): 1 si salió nuevo, 0 si no hay nada o ya se había avisado igual esta semana.</summary>
    private async Task<int> PublishAsync(string eventCode, string keyPrefix, IReadOnlyList<Detected> items, Dictionary<string, string?>? extra, CancellationToken ct)
    {
        if (items.Count == 0) return 0;
        var values = new Dictionary<string, string?>(extra ?? new())
        {
            ["count"] = items.Count.ToString(CultureInfo.InvariantCulture),
            ["items"] = InventoryAlertRules.ItemsText(items.Select(i => i.Text).ToList()),
        };
        var key = InventoryAlertRules.DedupKey(keyPrefix, clock.UtcNow, items.Select(i => i.Id));
        return await notifications.PublishAsync(eventCode, values, key, ct) ? 1 : 0;
    }
}
