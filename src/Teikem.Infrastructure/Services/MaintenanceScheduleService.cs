using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 4 (P4) — panel Mantenimiento preventivo: programas por vehículo o por tipo de vehículo, con disparador por
/// kilometraje, tiempo o ambos, y su evaluación Al día / Por vencer / Vencido / Sin historial (MaintenanceDue).
/// - Todo se lee bajo el filtro global de tenant; el TenantId del alta sale del principal.
/// - Un programa por tipo se evalúa vehículo por vehículo con su última OT CERRADA ligada al programa; sin ninguna, el
///   estatus es 'Sin historial'. Un programa por vehículo usa además LastServiceKm/LastServiceDate como respaldo.
/// - No hay generación automática de OT (no hay job). Nunca DELETE: quitar = IsActive 0.
/// - Ningún desbordamiento DECIMAL llega a SQL: FleetRules.DecimalError (12,1) antes de guardar.
/// </summary>
public sealed class MaintenanceScheduleService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    public const string TargetMessage = "Indique el vehículo o el tipo de vehículo del programa, no ambos.";
    public const string TriggerRequiredMessage = "El disparador del programa es obligatorio.";
    public const string LastServiceOnlyForVehicleMessage =
        "El último servicio solo se captura en programas de un vehículo; en los de tipo se toma de sus órdenes de trabajo cerradas.";
    public const string ConflictMessage = "El programa de mantenimiento no se pudo guardar; recargue e intente de nuevo.";

    private sealed record VehicleRow(int VehicleId, Guid PublicId, string Code, int? VehicleTypeLookupId, decimal? CurrentOdometerKm);

    // ---------------------------------------------------------------- lista

    public async Task<IReadOnlyList<MaintenanceScheduleDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var query = db.MaintenanceSchedules.AsNoTracking();
        if (!includeInactive) query = query.Where(s => s.IsActive);
        var rows = await query.OrderBy(s => s.Name).ThenBy(s => s.MaintenanceScheduleId).ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<MaintenanceScheduleDto>();

        var vehicleIds = rows.Where(s => s.VehicleId.HasValue).Select(s => s.VehicleId!.Value).Distinct().ToList();
        var vehicles = await db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.VehicleId))
            .Select(v => new { v.VehicleId, v.PublicId, v.Code }).ToDictionaryAsync(v => v.VehicleId, ct);

        var list = new List<MaintenanceScheduleDto>(rows.Count);
        foreach (var s in rows)
        {
            var v = s.VehicleId is int vid ? vehicles.GetValueOrDefault(vid) : null;
            list.Add(await ToDtoAsync(s, v?.PublicId, v?.Code, ct));
        }
        return list;
    }

    // ---------------------------------------------------------------- panel "Mantenimiento preventivo"

    public async Task<IReadOnlyList<MaintenanceDueDto>> GetDueAsync(MaintenanceDueQuery q, CancellationToken ct)
    {
        var stateFilter = SplitCodes(q.Status);
        foreach (var state in stateFilter)
            if (!MaintenanceDue.IsKnownState(state)) throw new ValidationException("status", $"Estatus desconocido: '{state}'.");

        // Vehículos activos y no terminales (bajo el filtro de tenant).
        var terminalIds = await TerminalStatusIdsAsync(StatusDomains.VehicleStatus, ct);
        var vehicleQuery = db.Vehicles.AsNoTracking().Where(v => v.IsActive && !terminalIds.Contains(v.StatusCodeId));
        if (q.VehiclePublicId is Guid vpid)
        {
            var only = await db.ResolveVehicleAsync(vpid, false, ct);
            vehicleQuery = vehicleQuery.Where(v => v.VehicleId == only.VehicleId);
        }
        var vehicles = await vehicleQuery
            .Select(v => new VehicleRow(v.VehicleId, v.PublicId, v.Code, v.VehicleTypeLookupId, v.CurrentOdometerKm))
            .ToListAsync(ct);
        if (vehicles.Count == 0) return Array.Empty<MaintenanceDueDto>();

        var schedules = await db.MaintenanceSchedules.AsNoTracking().Where(s => s.IsActive).ToListAsync(ct);
        if (schedules.Count == 0) return Array.Empty<MaintenanceDueDto>();

        // Expansión programa → vehículos: el vehículo del programa, o todos los del tipo.
        var pairs = new List<(MaintenanceSchedule Schedule, VehicleRow Vehicle)>();
        foreach (var s in schedules)
        {
            if (s.VehicleId is int vid)
            {
                var v = vehicles.FirstOrDefault(x => x.VehicleId == vid);
                if (v is not null) pairs.Add((s, v));
            }
            else if (s.VehicleTypeLookupId is int typeId)
                pairs.AddRange(vehicles.Where(x => x.VehicleTypeLookupId == typeId).Select(x => (s, x)));
        }
        if (pairs.Count == 0) return Array.Empty<MaintenanceDueDto>();

        // Última OT CERRADA activa por (programa, vehículo), en una sola consulta.
        var closedId = await db.StatusIdAsync(StatusDomains.WorkOrderStatus, WorkOrderStatuses.Closed, ct);
        var scheduleIds = pairs.Select(p => p.Schedule.MaintenanceScheduleId).Distinct().ToList();
        var pairVehicleIds = pairs.Select(p => p.Vehicle.VehicleId).Distinct().ToList();
        var closed = await db.MaintenanceWorkOrders.AsNoTracking()
            .Where(w => w.IsActive && w.StatusCodeId == closedId && w.MaintenanceScheduleId != null
                        && scheduleIds.Contains(w.MaintenanceScheduleId.Value) && pairVehicleIds.Contains(w.VehicleId))
            .Select(w => new { w.WorkOrderId, ScheduleId = w.MaintenanceScheduleId!.Value, w.VehicleId, w.Number, w.CompletedDate, w.OdometerKm })
            .ToListAsync(ct);
        var lastByPair = closed
            .GroupBy(w => (w.ScheduleId, w.VehicleId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(w => w.CompletedDate ?? DateOnly.MinValue).ThenByDescending(w => w.WorkOrderId).First());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = new List<MaintenanceDueDto>(pairs.Count);
        foreach (var (s, v) in pairs)
        {
            var trigger = (await lookups.GetAsync(s.TriggerLookupId, ct))?.InternalCode ?? string.Empty;
            var last = lastByPair.GetValueOrDefault((s.MaintenanceScheduleId, v.VehicleId));
            var perVehicle = s.VehicleId.HasValue;
            // Respaldo por dimensión: la OT cerrada manda; el programa por vehículo aporta su último servicio capturado.
            var lastKm = last?.OdometerKm ?? (perVehicle ? s.LastServiceKm : null);
            var lastDate = last?.CompletedDate ?? (perVehicle ? s.LastServiceDate : null);

            var due = MaintenanceDue.Evaluate(trigger, s.IntervalKm, s.IntervalDays, lastKm, lastDate, v.CurrentOdometerKm, today);
            if (stateFilter.Count > 0 && !stateFilter.Contains(due.State)) continue;

            result.Add(new MaintenanceDueDto(s.MaintenanceScheduleId, s.Name, v.PublicId, v.Code, trigger, s.IntervalKm, s.IntervalDays,
                lastKm, lastDate, last?.Number, v.CurrentOdometerKm, due.NextDueKm, due.NextDueDate, due.KmRemaining, due.DaysRemaining, due.State));
        }

        // Lo más urgente primero; luego vehículo y programa.
        return result
            .OrderByDescending(r => MaintenanceDue.Severity(r.State))
            .ThenBy(r => r.VehicleCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ScheduleName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ---------------------------------------------------------------- alta

    public async Task<MaintenanceScheduleDto> CreateAsync(MaintenanceScheduleRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var errors = new Dictionary<string, string[]>();

        var name = NormalizeName(req.Name, errors);
        if (req.Name is null && !errors.ContainsKey("name")) errors["name"] = new[] { MaintenanceDue.NameRequiredMessage };

        var hasVehicle = req.VehiclePublicId.HasValue;
        var hasType = !string.IsNullOrWhiteSpace(req.VehicleType);
        if (hasVehicle == hasType) errors["vehiclePublicId"] = new[] { TargetMessage };
        int? typeId = hasType && !hasVehicle ? await VehicleTypeIdAsync(req.VehicleType!, errors, ct) : null;

        string? trigger = null;
        int? triggerId = null;
        if (string.IsNullOrWhiteSpace(req.Trigger)) errors["trigger"] = new[] { TriggerRequiredMessage };
        else (trigger, triggerId) = await TriggerAsync(req.Trigger, errors, ct);

        var intervalKm = req.ClearIntervalKm == true ? null : req.IntervalKm;
        var intervalDays = req.ClearIntervalDays == true ? null : req.IntervalDays;
        if (hasType && !hasVehicle && (req.LastServiceKm.HasValue || req.LastServiceDate.HasValue))
            errors["lastServiceKm"] = new[] { LastServiceOnlyForVehicleMessage };
        if (trigger is not null) ValidateNumbers(trigger, intervalKm, intervalDays, req.LastServiceKm, req.LastServiceDate, errors);
        if (errors.Count > 0) throw new ValidationException(errors);

        int? vehicleId = null;
        if (hasVehicle) vehicleId = (await db.ResolveVehicleAsync(req.VehiclePublicId!.Value, false, ct)).VehicleId;

        var schedule = new MaintenanceSchedule
        {
            TenantId = tenantId, VehicleId = vehicleId, VehicleTypeLookupId = typeId, Name = name!, TriggerLookupId = triggerId!.Value,
            IntervalKm = intervalKm, IntervalDays = intervalDays,
            LastServiceKm = vehicleId.HasValue ? req.LastServiceKm : null,
            LastServiceDate = vehicleId.HasValue ? req.LastServiceDate : null,
            IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        db.MaintenanceSchedules.Add(schedule);
        await db.SaveGuardedAsync(ConflictMessage, ct);
        return await GetAsync(schedule.MaintenanceScheduleId, ct);
    }

    // ---------------------------------------------------------------- edición en línea

    /// <summary>
    /// PATCH: null = sin cambio. Si llega el vehículo o el tipo, reemplaza el objetivo (exactamente uno); al pasar a un
    /// programa por tipo se limpia el último servicio capturado (era de un vehículo). El intervalo se revalida contra el
    /// disparador resultante (el que llega o el actual).
    /// </summary>
    public async Task<MaintenanceScheduleDto> UpdateAsync(int id, MaintenanceScheduleRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var name = NormalizeName(req.Name, errors);

        var hasVehicle = req.VehiclePublicId.HasValue;
        var hasType = !string.IsNullOrWhiteSpace(req.VehicleType);
        if (hasVehicle && hasType) errors["vehiclePublicId"] = new[] { TargetMessage };
        int? typeId = hasType && !hasVehicle ? await VehicleTypeIdAsync(req.VehicleType!, errors, ct) : null;

        string? newTrigger = null;
        int? newTriggerId = null;
        if (req.Trigger is not null)
        {
            if (string.IsNullOrWhiteSpace(req.Trigger)) errors["trigger"] = new[] { TriggerRequiredMessage };
            else (newTrigger, newTriggerId) = await TriggerAsync(req.Trigger, errors, ct);
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        var schedule = await db.MaintenanceSchedules.FirstOrDefaultAsync(s => s.MaintenanceScheduleId == id, ct)
                       ?? throw new NotFoundException("Programa de mantenimiento");

        if (hasVehicle)
        {
            var vehicle = await db.ResolveVehicleAsync(req.VehiclePublicId!.Value, false, ct);
            schedule.VehicleId = vehicle.VehicleId;
            schedule.VehicleTypeLookupId = null;
        }
        else if (typeId is int t)
        {
            schedule.VehicleTypeLookupId = t;
            schedule.VehicleId = null;
            schedule.LastServiceKm = null;
            schedule.LastServiceDate = null;
        }

        if (name is not null) schedule.Name = name;
        if (newTriggerId is int tid) schedule.TriggerLookupId = tid;
        if (req.ClearIntervalKm == true) schedule.IntervalKm = null; else if (req.IntervalKm.HasValue) schedule.IntervalKm = req.IntervalKm;
        if (req.ClearIntervalDays == true) schedule.IntervalDays = null; else if (req.IntervalDays.HasValue) schedule.IntervalDays = req.IntervalDays;
        if (req.LastServiceKm.HasValue || req.LastServiceDate.HasValue)
        {
            if (schedule.VehicleId is null) throw new ValidationException("lastServiceKm", LastServiceOnlyForVehicleMessage);
            if (req.LastServiceKm.HasValue) schedule.LastServiceKm = req.LastServiceKm;
            if (req.LastServiceDate.HasValue) schedule.LastServiceDate = req.LastServiceDate;
        }

        // Validación sobre el resultado (lo que queda guardado), no solo sobre lo que llegó.
        var trigger = newTrigger ?? (await lookups.GetAsync(schedule.TriggerLookupId, ct))?.InternalCode ?? string.Empty;
        ValidateNumbers(trigger, schedule.IntervalKm, schedule.IntervalDays, schedule.LastServiceKm, schedule.LastServiceDate, errors);
        if (errors.Count > 0) throw new ValidationException(errors);

        await db.SaveGuardedAsync(ConflictMessage, ct);
        return await GetAsync(schedule.MaintenanceScheduleId, ct);
    }

    /// <summary>Checkbox Activo del programa (IsActive reversible). Un programa inactivo no aparece en el panel.</summary>
    public async Task SetActiveAsync(int id, bool active, CancellationToken ct)
    {
        var schedule = await db.MaintenanceSchedules.FirstOrDefaultAsync(s => s.MaintenanceScheduleId == id, ct)
                       ?? throw new NotFoundException("Programa de mantenimiento");
        if (schedule.IsActive == active) return;
        schedule.IsActive = active;
        await db.SaveGuardedAsync(ConflictMessage, ct);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<MaintenanceScheduleDto> GetAsync(int id, CancellationToken ct)
    {
        var s = await db.MaintenanceSchedules.AsNoTracking().FirstOrDefaultAsync(x => x.MaintenanceScheduleId == id, ct)
                ?? throw new NotFoundException("Programa de mantenimiento");
        Guid? vehiclePublicId = null;
        string? vehicleCode = null;
        if (s.VehicleId is int vid)
        {
            var v = await db.Vehicles.AsNoTracking().Where(x => x.VehicleId == vid).Select(x => new { x.PublicId, x.Code }).FirstOrDefaultAsync(ct);
            vehiclePublicId = v?.PublicId;
            vehicleCode = v?.Code;
        }
        return await ToDtoAsync(s, vehiclePublicId, vehicleCode, ct);
    }

    private async Task<MaintenanceScheduleDto> ToDtoAsync(MaintenanceSchedule s, Guid? vehiclePublicId, string? vehicleCode, CancellationToken ct)
    {
        var type = s.VehicleTypeLookupId is int tid ? await lookups.GetAsync(tid, ct) : null;
        var trigger = await lookups.GetAsync(s.TriggerLookupId, ct);
        return new MaintenanceScheduleDto(s.MaintenanceScheduleId, s.Name, vehiclePublicId, vehicleCode,
            type?.InternalCode, Label(type), trigger?.InternalCode ?? string.Empty, Label(trigger) ?? string.Empty,
            s.IntervalKm, s.IntervalDays, s.LastServiceKm, s.LastServiceDate, s.IsActive, s.CreatedAtUtc);
    }

    private static void ValidateNumbers(string trigger, decimal? intervalKm, int? intervalDays, decimal? lastServiceKm, DateOnly? lastServiceDate,
        IDictionary<string, string[]> errors)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var (field, message) in MaintenanceDue.ValidateSchedule(trigger, intervalKm, intervalDays, lastServiceKm, lastServiceDate, today))
            errors.TryAdd(field, new[] { message });
        // Precisión DECIMAL(12,1): nunca un 500 por desbordamiento.
        if (!errors.ContainsKey("intervalKm") && FleetRules.DecimalError(intervalKm, 12, 1) is string ik) errors["intervalKm"] = new[] { ik };
        if (!errors.ContainsKey("lastServiceKm") && FleetRules.DecimalError(lastServiceKm, 12, 1) is string lk) errors["lastServiceKm"] = new[] { lk };
    }

    /// <summary>null = sin cambio; vacío = error de obligatorio; más de 150 = error.</summary>
    private static string? NormalizeName(string? raw, IDictionary<string, string[]> errors)
    {
        if (raw is null) return null;
        var name = raw.Trim();
        if (name.Length == 0) { errors["name"] = new[] { MaintenanceDue.NameRequiredMessage }; return null; }
        if (name.Length > MaintenanceDue.NameMaxLength) { errors["name"] = new[] { MaintenanceDue.NameTooLongMessage }; return null; }
        return name;
    }

    private async Task<int?> VehicleTypeIdAsync(string code, IDictionary<string, string[]> errors, CancellationToken ct)
    {
        var id = await lookups.TryGetIdAsync(LookupDomains.VehicleType, code.Trim().ToUpperInvariant(), ct);
        if (id is null) errors["vehicleType"] = new[] { $"Tipo de vehículo desconocido: '{code.Trim()}'." };
        return id;
    }

    private async Task<(string? Code, int? Id)> TriggerAsync(string raw, IDictionary<string, string[]> errors, CancellationToken ct)
    {
        var code = raw.Trim().ToUpperInvariant();
        var id = MaintenanceDue.IsKnownTrigger(code) ? await lookups.TryGetIdAsync(LookupDomains.MaintenanceTrigger, code, ct) : null;
        if (id is null) { errors["trigger"] = new[] { $"Disparador desconocido: '{raw.Trim()}'." }; return (null, null); }
        return (code, id);
    }

    private async Task<List<int>> TerminalStatusIdsAsync(string domain, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == domain && s.StageKind!.InternalCode == StageKinds.Terminal)
            .Select(s => s.StatusCodeId).ToListAsync(ct);

    private static List<string> SplitCodes(string[]? codes)
        => codes is null
            ? new List<string>()
            : codes.Where(c => c is not null)
                .SelectMany(c => c.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(c => c.ToUpperInvariant()).Distinct().ToList();

    private string? Label(LookupCode? l) => l is null ? null : MultilingualText.Resolve(l.LabelJson, tenant.Lang);
}
