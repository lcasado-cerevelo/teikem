using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P9) — citas de muelle (R19b, D30) del módulo CROSSDOCK (demo). Una cita reserva un muelle del almacén en una
/// ventana [inicio, fin) — sin fin = 60 minutos — para un aviso de llegada (ASN), un viaje o nada, nunca ambos.
/// - Alta y reprogramación: dirección compatible con el tipo de muelle (400), ventana y horizonte de ayer a +90 días (400),
///   muelle activo (422) y SIN SOLAPAMIENTO con las citas SCHEDULED/ARRIVED del muelle (409). La verificación corre con el
///   muelle bloqueado (InventoryQueries: el muelle es el último encabezado del orden del lote), así dos altas simultáneas
///   sobre el mismo horario dan un 200 y un 409.
/// - Estatus a mano por StatusService (historial DOCK_APPOINTMENT): ARRIVED, COMPLETED, NO_SHOW (el motivo va en el
///   comentario del historial, D30), CANCELLED y el regreso a SCHEDULED desde NO_SHOW (re-verifica el solapamiento).
///   DockAppointmentStatusEffect ocupa el muelle con ARRIVED y lo libera al cerrar la cita.
/// El TenantId sale del principal; la cita (con TenantId) se expone por id entero bajo el filtro global y el muelle (hija
/// sin TenantId) se resuelve SIEMPRE por su almacén filtrado (otro muelle → 404 'Muelle no encontrado.').
/// </summary>
public sealed class DockAppointmentService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses)
{
    public const int MaxRows = 500;
    public const string DockRequired = "Indique el muelle.";
    public const string NoActiveWarehouse = "La compañía no tiene almacenes activos.";
    public const string WarehouseRequired = "Indique el almacén: la compañía tiene más de uno.";

    // ================================================================ lectura

    public async Task<IReadOnlyList<DockAppointmentDto>> ListAsync(DockAppointmentQuery? q, CancellationToken ct)
    {
        q ??= new DockAppointmentQuery();
        var query = db.Set<DockAppointment>().AsNoTracking();
        if (q.WarehousePublicId is Guid wh)
        {
            var warehouseId = await db.Set<Warehouse>().AsNoTracking().Where(w => w.PublicId == wh)
                                  .Select(w => (int?)w.WarehouseId).FirstOrDefaultAsync(ct)
                              ?? throw new NotFoundException("Almacén");
            query = query.Where(a => a.WarehouseId == warehouseId);
        }
        if (q.DockId is int dockId) query = query.Where(a => a.WarehouseDockId == dockId);
        if (q.FromUtc is DateTime from)
        {
            var f = DockScheduleRules.AsUtc(from);
            // Las que terminan después de 'desde' (sin fin: inicio + 60 min).
            var fMinusDefault = f.AddMinutes(-DockScheduleRules.DefaultDurationMinutes);
            query = query.Where(a => (a.ScheduledEndUtc != null && a.ScheduledEndUtc > f) || (a.ScheduledEndUtc == null && a.ScheduledStartUtc > fMinusDefault));
        }
        if (q.ToUtc is DateTime to)
        {
            var t = DockScheduleRules.AsUtc(to);
            query = query.Where(a => a.ScheduledStartUtc < t);
        }
        if (q.Status is { Length: > 0 })
        {
            var codes = Codes(q.Status);
            var ids = await db.StatusCodes.AsNoTracking()
                .Where(s => s.Entity == StatusDomains.AppointmentStatus && codes.Contains(s.InternalCode))
                .Select(s => s.StatusCodeId).ToListAsync(ct);
            query = query.Where(a => ids.Contains(a.StatusCodeId));
        }
        var rows = await query.OrderBy(a => a.ScheduledStartUtc).ThenBy(a => a.DockAppointmentId).Take(MaxRows).ToListAsync(ct);
        return await BuildAsync(rows, ct);
    }

    public async Task<DockAppointmentDto> GetAsync(int id, CancellationToken ct)
    {
        var row = await SnapshotAsync(id, ct);
        return (await BuildAsync(new List<DockAppointment> { row }, ct))[0];
    }

    // ================================================================ alta

    public async Task<DockAppointmentDto> CreateAsync(DockAppointmentRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException(DockRequired);
        if (req.DockId is not int dockId) throw new ValidationException("dockId", DockRequired);
        var direction = DockScheduleRules.NormalizeDirection(req.Direction)
                        ?? throw new ValidationException("direction", DockScheduleRules.UnknownDirection(req.Direction));
        if (req.ScheduledStartUtc is not DateTime rawStart) throw new ValidationException("scheduledStartUtc", DockScheduleRules.StartRequired);
        var start = DockScheduleRules.AsUtc(rawStart);
        DateTime? end = req.ScheduledEndUtc is DateTime e ? DockScheduleRules.AsUtc(e) : null;
        if (DockScheduleRules.ValidateWindow(start, end, DateTime.UtcNow) is string windowError)
            throw new ValidationException("scheduledStartUtc", windowError);
        if (DockScheduleRules.ValidateReferences(req.AsnId is not null, req.TripPublicId is not null, direction) is string refError)
            throw new ValidationException(req.AsnId is not null && req.TripPublicId is not null ? "tripPublicId" : "asnId", refError);

        var warehouse = await ResolveWarehouseOrDefaultAsync(req.WarehousePublicId, ct);
        var dock = await ResolveDockAsync(warehouse.WarehouseId, dockId, ct);
        await EnsureDockUsableAsync(dock, direction, ct);

        int? asnId = null;
        if (req.AsnId is int requestedAsn) asnId = await ResolveAsnAsync(warehouse.WarehouseId, requestedAsn, ct);
        int? tripId = null;
        if (req.TripPublicId is Guid tripPublicId) tripId = (await db.ResolveTripAsync(tripPublicId, false, ct)).TripId;

        var directionId = await lookups.GetIdAsync(LookupDomains.DockDirection, direction, ct);
        var initial = await statuses.GetInitialAsync(StatusDomains.AppointmentStatus, ct);
        var tenantId = ((TenantContext)tenant).RequireTenantId();

        var id = await db.RunInTransactionAsync(async ct2 =>
        {
            // Muelle bloqueado: serializa las citas del muelle (dos altas del mismo horario → un 200 y un 409).
            var locked = await LockDockAsync(warehouse.WarehouseId, dock.WarehouseDockId, ct2);
            if (!locked.IsActive) throw new StatusRuleException(DockScheduleRules.DockInactive);
            await EnsureNoOverlapAsync(locked.WarehouseDockId, start, end, null, ct2);

            var appt = new DockAppointment
            {
                TenantId = tenantId, WarehouseId = warehouse.WarehouseId, WarehouseDockId = locked.WarehouseDockId,
                DirectionLookupId = directionId, AsnId = asnId, TripId = tripId,
                ScheduledStartUtc = start, ScheduledEndUtc = end, StatusCodeId = initial.StatusCodeId,
                CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
            };
            db.Set<DockAppointment>().Add(appt);
            await db.SaveGuardedAsync(DockScheduleRules.Overlap, ct2);
            var born = await statuses.TransitionAsync(StatusDomains.AppointmentStatus, EntityTypes.DockAppointment, appt.DockAppointmentId,
                null, initial.InternalCode, null, ct2);
            appt.StatusCodeId = born.StatusCodeId;
            await db.SaveGuardedAsync(DockScheduleRules.Overlap, ct2);
            return appt.DockAppointmentId;
        }, ct);
        return await GetAsync(id, ct);
    }

    // ================================================================ reprogramar

    /// <summary>
    /// Reprograma una cita SCHEDULED (422 si no): nuevo inicio/fin y, opcionalmente, otro muelle del MISMO almacén. Si solo
    /// cambia el inicio, la cita conserva su duración. Se re-verifica el solapamiento con los muelles bloqueados en orden de id.
    /// </summary>
    public async Task<DockAppointmentDto> RescheduleAsync(int id, DockAppointmentPatchRequest req, CancellationToken ct)
    {
        req ??= new DockAppointmentPatchRequest();
        var current = await SnapshotAsync(id, ct);
        await EnsureStatusAsync(current.StatusCodeId, AppointmentStatuses.Scheduled, DockScheduleRules.NotReschedulable, ct);

        var start = req.ScheduledStartUtc is DateTime s ? DockScheduleRules.AsUtc(s) : current.ScheduledStartUtc;
        DateTime? end;
        if (req.ScheduledEndUtc is DateTime e) end = DockScheduleRules.AsUtc(e);
        else if (req.ScheduledStartUtc is not null && current.ScheduledEndUtc is DateTime oldEnd) end = start + (oldEnd - current.ScheduledStartUtc);
        else end = current.ScheduledEndUtc;
        if (DockScheduleRules.ValidateWindow(start, end, DateTime.UtcNow) is string windowError)
            throw new ValidationException("scheduledStartUtc", windowError);

        var direction = (await lookups.GetAsync(current.DirectionLookupId, ct))?.InternalCode ?? string.Empty;
        var targetDockId = req.DockId ?? current.WarehouseDockId;
        if (targetDockId != current.WarehouseDockId)
        {
            var target = await ResolveDockAsync(current.WarehouseId, targetDockId, ct);
            await EnsureDockUsableAsync(target, direction, ct);
        }

        await db.RunInTransactionAsync(async ct2 =>
        {
            // Muelles en orden de id (el de origen y el de destino) para no invertir el bloqueo entre dos reprogramaciones.
            WarehouseDock? targetDock = null;
            foreach (var dockId in new[] { current.WarehouseDockId, targetDockId }.Distinct().OrderBy(x => x))
            {
                var locked = await LockDockAsync(current.WarehouseId, dockId, ct2);
                if (dockId == targetDockId) targetDock = locked;
            }
            if (!targetDock!.IsActive) throw new StatusRuleException(DockScheduleRules.DockInactive);

            var appt = await db.Set<DockAppointment>().FirstOrDefaultAsync(a => a.DockAppointmentId == id, ct2)
                       ?? throw new NotFoundException("Cita", feminine: true);
            await EnsureStatusAsync(appt.StatusCodeId, AppointmentStatuses.Scheduled, DockScheduleRules.NotReschedulable, ct2);
            await EnsureNoOverlapAsync(targetDockId, start, end, id, ct2);

            appt.WarehouseDockId = targetDockId;
            appt.ScheduledStartUtc = start;
            appt.ScheduledEndUtc = end;
            await db.SaveGuardedAsync(DockScheduleRules.Overlap, ct2);
        }, ct);
        return await GetAsync(id, ct);
    }

    // ================================================================ estatus

    /// <summary>
    /// Cambia el estatus a mano (StatusService valida la transición: NO_SHOW y CANCELLED solo desde SCHEDULED; COMPLETED
    /// después de ARRIVED). El comentario queda en el historial (motivo del NO_SHOW). Volver a SCHEDULED re-verifica el
    /// solapamiento. El efecto ocupa o libera el muelle, que ya está bloqueado.
    /// </summary>
    public async Task<DockAppointmentDto> SetStatusAsync(int id, DockAppointmentStatusRequest req, CancellationToken ct)
    {
        var target = DockScheduleRules.NormalizeStatus(req?.Status)
                     ?? throw new ValidationException("status", DockScheduleRules.UnknownStatus(req?.Status));
        var comment = string.IsNullOrWhiteSpace(req?.Comment) ? null : req!.Comment!.Trim();
        if (comment is { Length: > StatusService.CommentMaxLength })
            throw new ValidationException("comment", StatusService.CommentTooLongMessage);
        var current = await SnapshotAsync(id, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            await LockDockAsync(current.WarehouseId, current.WarehouseDockId, ct2);
            var appt = await db.Set<DockAppointment>().FirstOrDefaultAsync(a => a.DockAppointmentId == id, ct2)
                       ?? throw new NotFoundException("Cita", feminine: true);
            if (string.Equals(target, AppointmentStatuses.Scheduled, StringComparison.OrdinalIgnoreCase))
                await EnsureNoOverlapAsync(appt.WarehouseDockId, appt.ScheduledStartUtc, appt.ScheduledEndUtc, appt.DockAppointmentId, ct2);

            var to = await statuses.TransitionAsync(StatusDomains.AppointmentStatus, EntityTypes.DockAppointment, appt.DockAppointmentId,
                appt.StatusCodeId, target, comment, ct2);
            appt.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(id, ct);
    }

    // ================================================================ apoyo

    private async Task<DockAppointment> SnapshotAsync(int id, CancellationToken ct)
        => await db.Set<DockAppointment>().AsNoTracking().FirstOrDefaultAsync(a => a.DockAppointmentId == id, ct)
           ?? throw new NotFoundException("Cita", feminine: true);

    private async Task EnsureStatusAsync(int statusCodeId, string expected, string message, CancellationToken ct)
    {
        var code = await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct);
        if (!string.Equals(code, expected, StringComparison.OrdinalIgnoreCase)) throw new StatusRuleException(message);
    }

    /// <summary>409 si la ventana choca con otra cita SCHEDULED/ARRIVED del muelle (semiabierto; sin fin = 60 min).</summary>
    private async Task EnsureNoOverlapAsync(int dockId, DateTime start, DateTime? end, int? excludeId, CancellationToken ct)
    {
        var activeCodes = DockScheduleRules.ActiveStatuses.ToList();
        var activeIds = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.AppointmentStatus && activeCodes.Contains(s.InternalCode))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var newEnd = DockScheduleRules.EffectiveEnd(start, end);
        var startMinusDefault = start.AddMinutes(-DockScheduleRules.DefaultDurationMinutes);
        var existing = await db.Set<DockAppointment>().AsNoTracking()
            .Where(a => a.WarehouseDockId == dockId && activeIds.Contains(a.StatusCodeId)
                        && (excludeId == null || a.DockAppointmentId != excludeId)
                        && a.ScheduledStartUtc < newEnd
                        && ((a.ScheduledEndUtc != null && a.ScheduledEndUtc > start) || (a.ScheduledEndUtc == null && a.ScheduledStartUtc > startMinusDefault)))
            .Select(a => new { a.ScheduledStartUtc, a.ScheduledEndUtc })
            .ToListAsync(ct);
        if (DockScheduleRules.OverlapsAny(start, end, existing.Select(x => (x.ScheduledStartUtc, x.ScheduledEndUtc))))
            throw new ConflictException(DockScheduleRules.Overlap);
    }

    /// <summary>Muelle activo (422) y compatible con la dirección (400).</summary>
    private async Task EnsureDockUsableAsync(WarehouseDock dock, string direction, CancellationToken ct)
    {
        if (!dock.IsActive) throw new StatusRuleException(DockScheduleRules.DockInactive);
        var dockType = (await lookups.GetAsync(dock.DockTypeLookupId, ct))?.InternalCode ?? string.Empty;
        if (!DockScheduleRules.Compatible(dockType, direction))
            throw new ValidationException("direction", DockScheduleRules.Incompatible(dockType, direction));
    }

    /// <summary>Muelle (hija sin TenantId) SIEMPRE por su almacén filtrado: de otro almacén u otro tenant → 404.</summary>
    private async Task<WarehouseDock> ResolveDockAsync(int warehouseId, int dockId, CancellationToken ct)
        => await (from d in db.Set<WarehouseDock>().AsNoTracking()
                  join w in db.Set<Warehouse>().AsNoTracking() on d.WarehouseId equals w.WarehouseId
                  where d.WarehouseDockId == dockId && d.WarehouseId == warehouseId
                  select d).FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException("Muelle");

    /// <summary>ASN del tenant y del mismo almacén (404); cancelado o dado de baja → 422.</summary>
    private async Task<int> ResolveAsnAsync(int warehouseId, int asnId, CancellationToken ct)
    {
        var asn = await db.Set<Asn>().AsNoTracking().Where(a => a.AsnId == asnId && a.WarehouseId == warehouseId)
                      .Select(a => new { a.AsnId, a.IsActive, a.StatusCodeId }).FirstOrDefaultAsync(ct)
                  ?? throw new NotFoundException("Aviso de llegada");
        var cancelledId = await db.StatusIdAsync(StatusDomains.AsnStatus, AsnStatuses.Cancelled, ct);
        if (!asn.IsActive || asn.StatusCodeId == cancelledId) throw new StatusRuleException(DockScheduleRules.AsnNotActive);
        return asn.AsnId;
    }

    /// <summary>Almacén por PublicId (404) o, sin él, el único activo del tenant (ninguno → 422; más de uno → 400).</summary>
    private Task<Warehouse> ResolveWarehouseOrDefaultAsync(Guid? publicId, CancellationToken ct)
        => WmsResolve.ResolveWarehouseOrDefaultAsync(db, publicId, ct); // única implementación (D26): 404 / 400 con más de uno / 422 sin ninguno

    /// <summary>DTOs por lotes (sin N+1): almacén, muelle con su tipo y estatus, ASN, viaje y estatus con etiqueta del tenant.</summary>
    private async Task<IReadOnlyList<DockAppointmentDto>> BuildAsync(IReadOnlyList<DockAppointment> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return Array.Empty<DockAppointmentDto>();
        var whIds = rows.Select(r => r.WarehouseId).Distinct().ToList();
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.WarehouseId))
            .ToDictionaryAsync(w => w.WarehouseId, w => w.PublicId, ct);
        var dockIds = rows.Select(r => r.WarehouseDockId).Distinct().ToList();
        var docks = await (from d in db.Set<WarehouseDock>().AsNoTracking()
                           join w in db.Set<Warehouse>().AsNoTracking() on d.WarehouseId equals w.WarehouseId
                           where dockIds.Contains(d.WarehouseDockId)
                           select new { d.WarehouseDockId, d.Code, d.DockTypeLookupId, d.StatusCodeId })
            .ToDictionaryAsync(d => d.WarehouseDockId, ct);
        var asnIds = rows.Where(r => r.AsnId != null).Select(r => r.AsnId!.Value).Distinct().ToList();
        var asns = await db.Set<Asn>().AsNoTracking().Where(a => asnIds.Contains(a.AsnId))
            .ToDictionaryAsync(a => a.AsnId, a => a.Reference, ct);
        var tripIds = rows.Where(r => r.TripId != null).Select(r => r.TripId!.Value).Distinct().ToList();
        var trips = await db.Trips.AsNoTracking().Where(t => tripIds.Contains(t.TripId))
            .Select(t => new { t.TripId, t.PublicId, t.Code }).ToDictionaryAsync(t => t.TripId, ct);
        var apptStatus = await StatusMapAsync(StatusDomains.AppointmentStatus, ct);
        var dockStatus = await StatusMapAsync(StatusDomains.DockStatus, ct);

        var result = new List<DockAppointmentDto>(rows.Count);
        foreach (var r in rows)
        {
            var d = docks.GetValueOrDefault(r.WarehouseDockId);
            var dockType = d is null ? null : await lookups.GetAsync(d.DockTypeLookupId, ct);
            var dir = await lookups.GetAsync(r.DirectionLookupId, ct);
            var s = apptStatus.GetValueOrDefault(r.StatusCodeId);
            var trip = r.TripId is int tid ? trips.GetValueOrDefault(tid) : null;
            result.Add(new DockAppointmentDto(r.DockAppointmentId, warehouses.GetValueOrDefault(r.WarehouseId), r.WarehouseDockId,
                d?.Code ?? string.Empty, dockType?.InternalCode ?? string.Empty,
                d is null ? string.Empty : dockStatus.GetValueOrDefault(d.StatusCodeId)?.Code ?? string.Empty,
                dir?.InternalCode ?? string.Empty, dir is null ? string.Empty : MultilingualText.Resolve(dir.LabelJson, tenant.Lang),
                r.ScheduledStartUtc, r.ScheduledEndUtc,
                r.AsnId, r.AsnId is int aid ? asns.GetValueOrDefault(aid) : null,
                trip?.PublicId, trip?.Code,
                s?.Code ?? string.Empty, s?.Label ?? string.Empty, s?.Color));
        }
        return result;
    }

    private sealed record StatusInfo(string Code, string Label, string? Color);

    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(string domain, CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == domain).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, StatusCodeOverride>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        return codes.ToDictionary(c => c.StatusCodeId, c =>
        {
            var o = overrides.GetValueOrDefault(c.StatusCodeId);
            return new StatusInfo(c.InternalCode, MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, o?.CustomLabelJson), tenant.Lang),
                o?.CustomColorHex ?? c.ColorHex);
        });
    }

    private static List<string> Codes(IEnumerable<string> raw)
        => raw.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).Distinct().ToList();

    // ================================================================ adaptador a la costura de P0 (InventoryQueries)

    /// <summary>Muelle con UPDLOCK (JOIN al almacén del tenant), tracked; último encabezado del orden de bloqueo del lote.</summary>
    private Task<WarehouseDock> LockDockAsync(int warehouseId, int dockId, CancellationToken ct) => db.LockDockAsync(warehouseId, dockId, ct);
}
