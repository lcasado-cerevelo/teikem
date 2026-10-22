using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Migration.Colavora;
using Teikem.Domain.Trips;

namespace Teikem.Infrastructure.Migration.Colavora;

/// <summary>
/// 2026-10-22 — (a) ZIP y zona de los envíos que Colavora manda sin ZIP del consignatario (historial primero, municipio después;
/// ver ColavoraZipInference), tanto al importar como en una reparación de lo ya migrado, y (b) la ruta vigente con sus paradas
/// para los viajes migrados (M-…), que antes quedaban con envíos enlazados pero sin paradas (Despacho los mostraba vacíos).
/// </summary>
public sealed partial class ColavoraImportService
{
    private const string MsgFromHistory = "Sin código postal en Colavora: se tomó del historial del consignatario (inferido).";
    private const string MsgFromTown = "Sin código postal en Colavora: la zona se infirió por el municipio del texto de la dirección (inferido).";
    private const string MsgUnresolved = "Sin código postal en Colavora y sin historial ni municipio que lo resuelva: queda sin zona.";

    private readonly record struct MissingZip(int Key, string Label, string? Name, string? Line1, string? Line2);

    /// <summary>
    /// Infiere el ZIP/zona de cada envío sin ZIP: historial del consignatario en Colavora (solo lectura) y, si no, el municipio que
    /// nombra el texto. Anota cada resultado en el reporte (inferidos y sin zona, con la clave del envío y su dirección).
    /// </summary>
    private async Task<Dictionary<int, ColavoraZipInference.Inferred>> InferMissingZipsAsync(RunState s, IReadOnlyList<MissingZip> items, CancellationToken ct)
    {
        var result = new Dictionary<int, ColavoraZipInference.Inferred>();
        if (items.Count == 0) return result;
        s.Municipalities ??= new ColavoraZipInference.MunicipalityIndex(s.CityByZip, s.ZoneIdByZip);

        var names = items.Where(i => !string.IsNullOrWhiteSpace(i.Name)).Select(i => i.Name!.Trim()).ToList();
        var history = names.Count == 0 ? new List<CvConsigneeZip>() : await s.Reader.ReadConsigneeZipHistoryAsync(s.CompanyId, names, ct);
        var byName = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var byNameAddress = new Dictionary<(string, string), HashSet<string>>();
        foreach (var h in history)
        {
            if (ColavoraRules.NormalizeZip(h.Zip) is not { } zip) continue;
            var n = ColavoraZipInference.Fold(h.Name);
            if (!byName.TryGetValue(n, out var set)) byName[n] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(zip);
            var key = (n, ColavoraZipInference.Fold(h.AddressLine1));
            if (!byNameAddress.TryGetValue(key, out var set2)) byNameAddress[key] = set2 = new HashSet<string>(StringComparer.Ordinal);
            set2.Add(zip);
        }

        var none = new HashSet<string>(StringComparer.Ordinal);
        foreach (var it in items)
        {
            var n = ColavoraZipInference.Fold(it.Name);
            var inferred = ColavoraZipInference.Infer(it.Name, it.Line1, it.Line2,
                n.Length > 0 && byNameAddress.TryGetValue((n, ColavoraZipInference.Fold(it.Line1)), out var a) ? a : none,
                n.Length > 0 && byName.TryGetValue(n, out var b) ? b : none,
                s.Municipalities, s.ZoneIdByZip, s.CityByZip);
            var where = $"{it.Label} · {it.Name} · {it.Line1}";
            if (inferred is null)
            {
                s.ZipUnresolved++;
                s.Report.Warn(ColavoraEntities.Orders, where, MsgUnresolved);
                continue;
            }
            result[it.Key] = inferred;
            if (inferred.Source == ColavoraZipInference.SourceHistory) { s.ZipFromHistory++; s.Report.Warn(ColavoraEntities.Orders, $"{where} → {inferred.Detail}", MsgFromHistory); }
            else { s.ZipFromTown++; s.Report.Warn(ColavoraEntities.Orders, $"{where} → {inferred.Detail}", MsgFromTown); }
        }
        return result;
    }

    private void ReportInferredTotals(RunState s)
    {
        var total = s.ZipFromHistory + s.ZipFromTown + s.ZipUnresolved;
        if (total == 0) return;
        s.Report.AddInfo("Envíos sin código postal en Colavora", $"{total:N0}: {s.ZipFromHistory:N0} resueltos por el historial del consignatario, {s.ZipFromTown:N0} por el municipio del texto, {s.ZipUnresolved:N0} sin zona (ver las advertencias: cada una trae el envío, el consignatario y la dirección)");
    }

    // ================================================================ reparar lo ya migrado

    /// <summary>Envíos ya migrados sin zona y sin ZIP de entrega: se les aplica la misma inferencia y se guardan el ZIP, la ciudad y la zona (sin tocar nada más).</summary>
    private async Task RepairOrderZonesAsync(RunState s, CancellationToken ct)
    {
        if (s.TenantId is null) return;
        var tenant = s.TenantId.Value;
        var lastId = 0;
        long fixedCount = 0;
        var started = DateTime.UtcNow;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var stops = await db.OrderStops.Include(x => x.Order)
                .Where(x => x.OrderStopId > lastId && x.StopTypeLookupId == s.StopTypeDeliveryId && x.SnapPostalCode == null && x.Order!.DeliveryZoneId == null && x.Order.TenantId == tenant)
                .OrderBy(x => x.OrderStopId).Take(Math.Max(100, s.Config.Options.BatchSize)).ToListAsync(ct);
            if (stops.Count == 0) break;
            lastId = stops[^1].OrderStopId;
            var items = stops.Select(x => new MissingZip(x.OrderStopId, x.Order!.WaybillNumber, x.SnapName, x.SnapLine1, x.SnapLine2)).ToList();
            var inferred = await InferMissingZipsAsync(s, items, ct);
            foreach (var st in stops)
            {
                if (!inferred.TryGetValue(st.OrderStopId, out var inf) || inf.ZoneId == 0) continue;
                if (inf.Zip is not null) st.SnapPostalCode = inf.Zip;
                if (!string.IsNullOrWhiteSpace(inf.City)) st.SnapCity = inf.City!;
                st.Order!.DeliveryZoneId = inf.ZoneId;
                fixedCount++;
            }
            if (!s.DryRun)
            {
                db.SuppressAudit = true;
                try { await db.SaveChangesAsync(ct); }
                finally { db.SuppressAudit = false; }
            }
            db.ChangeTracker.Clear();
        }
        s.Report.AddInfo("Envíos ya migrados reparados (ZIP/zona)", $"{fixedCount:N0}{(s.DryRun ? " (simulación: no se guardó nada)" : string.Empty)} en {(DateTime.UtcNow - started):hh\\:mm\\:ss}");
        if (!s.Phases.Contains(ColavoraPhases.Orders)) ReportInferredTotals(s);   // con «ordenes» el total sale al final de esa fase
    }

    // ================================================================ rutas y paradas de los viajes migrados

    /// <summary>
    /// Los viajes M-… se crearon con sus envíos enlazados (TripOrder) pero sin ruta ni paradas, y Despacho muestra «La ruta no tiene
    /// paradas». Crea la versión vigente (ACTIVE: el viaje ya se despachó) y una parada por cada envío vigente del viaje, en el orden en
    /// que se asignó, con el estado de su parada de entrega (completada, fallida o pendiente). Idempotente: solo viajes sin ruta.
    /// </summary>
    private async Task CreateMissingRouteStopsAsync(RunState s, CancellationToken ct)
    {
        if (s.TenantId is null) return;
        const string E = ColavoraEntities.RouteStops;
        var routeStatus = await db.StatusCodes.AsNoTracking().Where(x => x.Entity == StatusDomains.RouteStatus).ToDictionaryAsync(x => x.InternalCode, x => x.StatusCodeId, StringComparer.OrdinalIgnoreCase, ct);
        var stopStatus = await db.StatusCodes.AsNoTracking().Where(x => x.Entity == StatusDomains.RouteStopStatus).ToDictionaryAsync(x => x.InternalCode, x => x.StatusCodeId, StringComparer.OrdinalIgnoreCase, ct);
        if (!routeStatus.TryGetValue(RouteStatuses.Active, out var activeId) || !routeStatus.TryGetValue(RouteStatuses.Draft, out var draftId)
            || !stopStatus.TryGetValue(RouteStopStatuses.Pending, out var pendingId) || !stopStatus.TryGetValue(RouteStopStatuses.Completed, out var completedId) || !stopStatus.TryGetValue(RouteStopStatuses.Failed, out var failedId))
        {
            s.Report.Warn(E, "catálogos", "Faltan estatus de ruta o de parada de ruta en Teikem (ejecute db-update); no se crearon paradas.");
            return;
        }
        var tripStatusCode = s.TripStatus.ToDictionary(kv => kv.Value, kv => kv.Key);
        var orderStopStatusCode = s.StopStatus.ToDictionary(kv => kv.Value, kv => kv.Key);

        var tripIds = await db.Trips.AsNoTracking().Where(t => t.Code.StartsWith("M-") && !db.Routes.Any(r => r.TripId == t.TripId)).Select(t => t.TripId).ToListAsync(ct);
        long routes = 0, stopsCreated = 0;
        foreach (var chunk in tripIds.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var trips = await db.Trips.AsNoTracking().Where(t => chunk.Contains(t.TripId)).Select(t => new { t.TripId, t.StatusCodeId, t.CreatedAtUtc }).ToListAsync(ct);
            var links = await (from l in db.TripOrders.AsNoTracking()
                               where chunk.Contains(l.TripId) && l.IsCurrent
                               join st in db.OrderStops.AsNoTracking() on l.TransportOrderId equals st.TransportOrderId
                               where st.StopTypeLookupId == s.StopTypeDeliveryId
                               select new { l.TripId, l.TransportOrderId, l.AssignedAtUtc, st.OrderStopId, st.StatusCodeId, st.CompletedAtUtc }).ToListAsync(ct);
            foreach (var t in trips)
            {
                var mine = links.Where(x => x.TripId == t.TripId).GroupBy(x => x.TransportOrderId).Select(g => g.OrderBy(x => x.OrderStopId).First())
                    .OrderBy(x => x.AssignedAtUtc).ThenBy(x => x.TransportOrderId).ToList();
                if (mine.Count == 0) continue;
                var code = tripStatusCode.GetValueOrDefault(t.StatusCodeId);
                var frozen = code is TripStatuses.Dispatched or TripStatuses.InProgress or TripStatuses.Completed;
                var route = new Route { TripId = t.TripId, Version = 1, IsActive = true, StatusCodeId = frozen ? activeId : draftId, StopCount = mine.Count, CreatedAtUtc = t.CreatedAtUtc };
                var seq = 0;
                foreach (var m in mine)
                {
                    var oc = orderStopStatusCode.GetValueOrDefault(m.StatusCodeId);
                    var statusId = oc == StopStatuses.Completed ? completedId : oc == StopStatuses.Failed ? failedId : pendingId;
                    route.Stops.Add(new RouteStop
                    {
                        OrderStopId = m.OrderStopId, Sequence = ++seq, StatusCodeId = statusId,
                        ActualArrivalUtc = statusId == pendingId ? null : m.CompletedAtUtc, ActualDepartureUtc = statusId == pendingId ? null : m.CompletedAtUtc,
                    });
                }
                db.Routes.Add(route);
                routes++;
                stopsCreated += mine.Count;
            }
            if (!s.DryRun)
            {
                db.SuppressAudit = true;
                try { await db.SaveChangesAsync(ct); }
                finally { db.SuppressAudit = false; }
            }
            db.ChangeTracker.Clear();
        }
        s.Report.CountRead(E, tripIds.Count);
        s.Report.CountCreated(E, stopsCreated);
        s.Report.AddInfo("Rutas migradas con paradas creadas", $"{routes:N0} rutas, {stopsCreated:N0} paradas{(s.DryRun ? " (simulación: no se guardó nada)" : string.Empty)}");
    }
}
