using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Cuántos avisos nuevos salieron por evento en una revisión (0 = nada nuevo que avisar o ya se había avisado esta semana).</summary>
public sealed record FleetAlertScanResult(int DocsExpiring, int DocsExpired, int MaintenanceDueSoon, int MaintenanceOverdue)
{
    public int Total => DocsExpiring + DocsExpired + MaintenanceDueSoon + MaintenanceOverdue;
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
            await PublishAsync(FleetEventCodes.MaintenanceOverdue, "FMO", overdue, null, ct));
        if (result.Total > 0) await db.SaveChangesAsync(ct);
        return result;
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
