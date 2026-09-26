using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 4 (P4) — panel Órdenes de trabajo (OT) de mantenimiento. EntityType WORK_ORDER (el ya sembrado), estatus
/// WorkOrderStatus: OPEN (inicial) → IN_PROGRESS → CLOSED (terminal); CANCELLED terminal. Solo vía StatusService.
/// - Número automático 'OT-#####' por compañía con NumberSequence Kind WORKORDER (ClientId NULL): EnsureAsync en autocommit
///   antes de la transacción y NextAsync dentro (bloqueo de fila hasta el commit: altas concurrentes consecutivas, sin huecos).
/// - Editar encabezado y tareas exige la capacidad EDIT_WORK_ORDER (por defecto denegada en CLOSED/CANCELLED → 422).
/// - Costos: con tareas activas son la suma de sus tareas (el encabezado no se captura); sin tareas, directo en el
///   encabezado. TotalCost lo calcula SQL (columna computada); aquí se devuelve Labor + Partes.
/// - Cerrar exige tareas activas completas (422) y odómetro si el programa es por kilometraje (400) (MaintenanceDue.ValidateClose).
///   El efecto en el vehículo (MAINTENANCE/ACTIVE, odómetro, último servicio) lo aplica WorkOrderStatusEffect.
/// - Las tareas no llevan TenantId ni PublicId: se alcanzan SOLO a través de su OT del tenant (404 si el id es de otra OT).
/// </summary>
public sealed class MaintenanceWorkOrderService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    INumberSequenceService sequences)
{
    public const string NumberPattern = "OT-#####";
    public const int VendorMaxLength = 150;

    public const string NumberTakenMessage = "Ya existe una orden de trabajo con ese número; intente de nuevo.";
    public const string TypeRequiredMessage = "El tipo de mantenimiento es obligatorio.";
    public const string ScheduleNotApplicableMessage = "El programa de mantenimiento no aplica a este vehículo.";
    public const string ScheduleInactiveMessage = "El programa de mantenimiento está inactivo.";
    public const string CorrectiveWithScheduleMessage = "Una orden correctiva no se asocia a un programa preventivo.";
    public const string NegativeCostMessage = "Los costos no pueden ser negativos.";
    public const string NegativeOdometerMessage = "El odómetro no puede ser negativo.";
    public const string ImmutableMessage = "El número y el vehículo de la orden de trabajo se fijan al crearla.";
    public const string CostsFromTasksMessage = "La orden tiene tareas: los costos de labor y partes se calculan con la suma de sus tareas.";
    public const string TaskDescriptionRequiredMessage = "La descripción de la tarea es obligatoria.";
    public const string TaskDescriptionTooLongMessage = "La descripción de la tarea admite como máximo 250 caracteres.";
    public const string CompletedDateFutureMessage = "La fecha de cierre no puede ser futura.";
    public const string ToCodeRequiredMessage = "El estatus destino es obligatorio.";
    public const string VendorTooLongMessage = "El proveedor admite como máximo 150 caracteres.";

    /// <summary>Campos que el PATCH rechaza aunque lleguen en el cuerpo (van a Extra por no estar en el contrato).</summary>
    private static readonly string[] ImmutableOnPatch = { "number", "vehiclePublicId", "vehicleId" };

    private sealed record StatusInfo(string Code, string Label, string? Color, bool IsTerminal);

    // ---------------------------------------------------------------- lista

    public async Task<IReadOnlyList<WorkOrderListItemDto>> ListAsync(WorkOrderListQuery q, CancellationToken ct)
    {
        var statusMap = await StatusMapAsync(ct);
        var query = db.MaintenanceWorkOrders.AsNoTracking();
        if (!q.IncludeInactive) query = query.Where(w => w.IsActive);

        if (q.VehiclePublicId is Guid vpid)
        {
            var vehicle = await db.ResolveVehicleAsync(vpid, false, ct);
            query = query.Where(w => w.VehicleId == vehicle.VehicleId);
        }
        if (q.ScheduleId is int sid) query = query.Where(w => w.MaintenanceScheduleId == sid);

        var statusCodes = SplitCodes(q.Status);
        if (statusCodes.Count > 0)
        {
            var ids = new List<int>(statusCodes.Count);
            foreach (var code in statusCodes)
            {
                var hit = statusMap.FirstOrDefault(kv => string.Equals(kv.Value.Code, code, StringComparison.OrdinalIgnoreCase));
                if (hit.Value is null) throw new ValidationException("status", $"Estatus desconocido: '{code}'.");
                ids.Add(hit.Key);
            }
            query = query.Where(w => ids.Contains(w.StatusCodeId));
        }

        var typeCodes = SplitCodes(q.MaintenanceType);
        if (typeCodes.Count > 0)
        {
            var ids = new List<int>(typeCodes.Count);
            foreach (var code in typeCodes)
                ids.Add(await lookups.TryGetIdAsync(LookupDomains.MaintenanceType, code, ct)
                        ?? throw new ValidationException("maintenanceType", $"Tipo de mantenimiento desconocido: '{code}'."));
            query = query.Where(w => ids.Contains(w.MaintenanceTypeLookupId));
        }

        // Rango de fechas sobre la fecha programada; sin fecha programada, sobre la fecha de alta. from/to inclusivos.
        if (q.From is DateOnly from)
        {
            var fromUtc = from.ToDateTime(TimeOnly.MinValue);
            query = query.Where(w => (w.ScheduledDate != null && w.ScheduledDate >= from) || (w.ScheduledDate == null && w.CreatedAtUtc >= fromUtc));
        }
        // to = DateOnly.MaxValue no limita nada (y to+1 desbordaría): se omite el filtro superior.
        if (q.To is DateOnly to && to < DateOnly.MaxValue)
        {
            var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(w => (w.ScheduledDate != null && w.ScheduledDate <= to) || (w.ScheduledDate == null && w.CreatedAtUtc < toUtc));
        }

        var (skip, take) = OrderRules.NormalizePaging(q.Skip, q.Take);
        var rows = await query.OrderByDescending(w => w.WorkOrderId).Skip(skip).Take(take).ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<WorkOrderListItemDto>();

        var vehicleIds = rows.Select(w => w.VehicleId).Distinct().ToList();
        var vehicles = await db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.VehicleId))
            .Select(v => new { v.VehicleId, v.PublicId, v.Code }).ToDictionaryAsync(v => v.VehicleId, ct);
        var scheduleIds = rows.Where(w => w.MaintenanceScheduleId.HasValue).Select(w => w.MaintenanceScheduleId!.Value).Distinct().ToList();
        var schedules = await db.MaintenanceSchedules.AsNoTracking().Where(s => scheduleIds.Contains(s.MaintenanceScheduleId))
            .Select(s => new { s.MaintenanceScheduleId, s.Name }).ToDictionaryAsync(s => s.MaintenanceScheduleId, s => s.Name, ct);

        var items = new List<WorkOrderListItemDto>(rows.Count);
        foreach (var w in rows)
        {
            var vehicle = vehicles.GetValueOrDefault(w.VehicleId);
            var type = await lookups.GetAsync(w.MaintenanceTypeLookupId, ct);
            var status = statusMap.GetValueOrDefault(w.StatusCodeId);
            items.Add(new WorkOrderListItemDto(w.WorkOrderId, w.PublicId, w.Number, vehicle?.PublicId ?? Guid.Empty, vehicle?.Code ?? string.Empty,
                type?.InternalCode ?? string.Empty, Label(type) ?? string.Empty,
                w.MaintenanceScheduleId, w.MaintenanceScheduleId is int s ? schedules.GetValueOrDefault(s) : null,
                status?.Code ?? string.Empty, status?.Label ?? string.Empty, status?.Color,
                w.ScheduledDate, w.CompletedDate, w.OdometerKm, w.Vendor, Total(w.LaborCost, w.PartsCost), w.IsActive));
        }
        return items;
    }

    // ---------------------------------------------------------------- ficha

    public async Task<WorkOrderDetailDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var w = await db.MaintenanceWorkOrders.AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct)
                ?? throw new NotFoundException("Orden de trabajo", feminine: true);
        var tasks = await db.MaintenanceTasks.AsNoTracking()
            .Where(t => t.WorkOrderId == w.WorkOrderId && t.IsActive)
            .OrderBy(t => t.MaintenanceTaskId).ToListAsync(ct);

        var vehicle = await db.Vehicles.AsNoTracking().Where(v => v.VehicleId == w.VehicleId)
            .Select(v => new { v.PublicId, v.Code }).FirstOrDefaultAsync(ct);
        string? scheduleName = null, scheduleTrigger = null;
        if (w.MaintenanceScheduleId is int sid)
        {
            var s = await db.MaintenanceSchedules.AsNoTracking().Where(x => x.MaintenanceScheduleId == sid)
                .Select(x => new { x.Name, x.TriggerLookupId }).FirstOrDefaultAsync(ct);
            scheduleName = s?.Name;
            scheduleTrigger = s is null ? null : (await lookups.GetAsync(s.TriggerLookupId, ct))?.InternalCode;
        }

        var statusMap = await StatusMapAsync(ct);
        var status = statusMap.GetValueOrDefault(w.StatusCodeId);
        var type = await lookups.GetAsync(w.MaintenanceTypeLookupId, ct);
        var currency = w.CurrencyLookupId is int cid ? await lookups.GetAsync(cid, ct) : null;
        var canEdit = await statuses.IsAllowedAsync(EntityTypes.WorkOrder, w.StatusCodeId, Capabilities.EditWorkOrder, ct);

        return new WorkOrderDetailDto(w.WorkOrderId, w.PublicId, w.Number, vehicle?.PublicId ?? Guid.Empty, vehicle?.Code ?? string.Empty,
            type?.InternalCode ?? string.Empty, Label(type) ?? string.Empty,
            w.MaintenanceScheduleId, scheduleName, scheduleTrigger,
            status?.Code ?? string.Empty, status?.Label ?? string.Empty, status?.IsTerminal ?? false,
            w.ScheduledDate, w.CompletedDate, w.OdometerKm, w.Vendor, w.LaborCost, w.PartsCost, Total(w.LaborCost, w.PartsCost),
            currency?.InternalCode, w.Notes, tasks.Count > 0, canEdit,
            tasks.Select(ToTaskDto).ToList(), w.IsActive, w.CreatedAtUtc, w.UpdatedAtUtc,
            Convert.ToBase64String(w.RowVersion ?? Array.Empty<byte>()));
    }

    // ---------------------------------------------------------------- alta

    public async Task<WorkOrderDetailDto> CreateAsync(WorkOrderCreateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var errors = new Dictionary<string, string[]>();

        var vendor = Text(req.Vendor, "vendor", VendorMaxLength, VendorTooLongMessage, errors);
        ValidateCost(req.LaborCost, "laborCost", errors);
        ValidateCost(req.PartsCost, "partsCost", errors);
        ValidateOdometer(req.OdometerKm, "odometerKm", errors);
        var currencyId = await CurrencyIdAsync(req.Currency, errors, ct);

        int? typeId = null;
        string? typeCode = null;
        if (!string.IsNullOrWhiteSpace(req.MaintenanceType))
        {
            typeCode = req.MaintenanceType.Trim().ToUpperInvariant();
            typeId = await lookups.TryGetIdAsync(LookupDomains.MaintenanceType, typeCode, ct);
            if (typeId is null) errors["maintenanceType"] = new[] { $"Tipo de mantenimiento desconocido: '{req.MaintenanceType.Trim()}'." };
        }
        else if (req.ScheduleId.HasValue)
        {
            // Con programa, PREVENTIVE por defecto.
            typeCode = MaintenanceTypes.Preventive;
            typeId = await lookups.GetIdAsync(LookupDomains.MaintenanceType, MaintenanceTypes.Preventive, ct);
        }
        else errors["maintenanceType"] = new[] { TypeRequiredMessage };
        if (errors.Count > 0) throw new ValidationException(errors);

        // Vehículo del tenant (404), activo y no terminal (409).
        var vehicle = await db.ResolveVehicleAsync(req.VehiclePublicId, false, ct);
        if (!vehicle.IsActive || await db.IsTerminalAsync(vehicle.StatusCodeId, ct)) throw new ConflictException(FleetQueries.VehicleInactiveMessage);

        if (req.ScheduleId is int scheduleId)
        {
            var schedule = await db.MaintenanceSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.MaintenanceScheduleId == scheduleId, ct)
                           ?? throw new NotFoundException("Programa de mantenimiento");
            if (!schedule.IsActive) throw new ValidationException("scheduleId", ScheduleInactiveMessage);
            var applies = schedule.VehicleId is int sv
                ? sv == vehicle.VehicleId
                : schedule.VehicleTypeLookupId is int st && st == vehicle.VehicleTypeLookupId;
            if (!applies) throw new ValidationException("scheduleId", ScheduleNotApplicableMessage);
            if (!string.Equals(typeCode, MaintenanceTypes.Preventive, StringComparison.OrdinalIgnoreCase))
                throw new ValidationException("maintenanceType", CorrectiveWithScheduleMessage);
        }

        // La fila del contador se asegura en autocommit, antes de la transacción (solo el UPDATE corre dentro).
        await sequences.EnsureAsync(NumberKinds.WorkOrder, null, ct);
        var initial = await statuses.GetInitialAsync(StatusDomains.WorkOrderStatus, ct);

        var publicId = await db.RunInTransactionAsync(async ct2 =>
        {
            var n = await sequences.NextAsync(NumberKinds.WorkOrder, null, ct2);
            var wo = new MaintenanceWorkOrder
            {
                TenantId = tenantId, VehicleId = vehicle.VehicleId, MaintenanceScheduleId = req.ScheduleId,
                Number = NumberFormat.Resolve(NumberPattern, n), MaintenanceTypeLookupId = typeId!.Value,
                StatusCodeId = initial.StatusCodeId, OdometerKm = req.OdometerKm, ScheduledDate = req.ScheduledDate,
                Vendor = vendor, LaborCost = req.LaborCost, PartsCost = req.PartsCost, CurrencyLookupId = currencyId,
                Notes = OptionalText(req.Notes), IsActive = true,
            };
            db.MaintenanceWorkOrders.Add(wo);
            await db.SaveGuardedAsync(NumberTakenMessage, ct2);
            // Historial de nacimiento (null → OPEN).
            var born = await statuses.TransitionAsync(StatusDomains.WorkOrderStatus, EntityTypes.WorkOrder, wo.WorkOrderId, null, initial.InternalCode, null, ct2);
            wo.StatusCodeId = born.StatusCodeId;
            await db.SaveGuardedAsync(NumberTakenMessage, ct2);
            return wo.PublicId;
        }, ct);

        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- edición en línea

    public async Task<WorkOrderDetailDto> UpdateAsync(Guid publicId, WorkOrderPatchRequest req, CancellationToken ct)
    {
        if (req.Extra is not null)
            foreach (var key in req.Extra.Keys)
            {
                var hit = ImmutableOnPatch.FirstOrDefault(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) throw new ValidationException(hit, ImmutableMessage);
            }

        var errors = new Dictionary<string, string[]>();
        var vendor = Text(req.Vendor, "vendor", VendorMaxLength, VendorTooLongMessage, errors);
        ValidateCost(req.LaborCost, "laborCost", errors);
        ValidateCost(req.PartsCost, "partsCost", errors);
        ValidateOdometer(req.OdometerKm, "odometerKm", errors);
        var currencyId = await CurrencyIdAsync(req.Currency, errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var wo = await LoadForWriteAsync(publicId, ct2);
            db.ApplyRowVersion(wo, req.RowVersion);
            await statuses.EnsureAllowedAsync(EntityTypes.WorkOrder, wo.StatusCodeId, Capabilities.EditWorkOrder, ct2);

            if (req.LaborCost.HasValue || req.PartsCost.HasValue)
            {
                var hasTasks = await db.MaintenanceTasks.AnyAsync(t => t.WorkOrderId == wo.WorkOrderId && t.IsActive, ct2);
                if (hasTasks) throw new ValidationException(req.LaborCost.HasValue ? "laborCost" : "partsCost", CostsFromTasksMessage);
            }

            if (req.ScheduledDate.HasValue) wo.ScheduledDate = req.ScheduledDate;
            if (req.OdometerKm.HasValue) wo.OdometerKm = req.OdometerKm;
            if (req.Vendor is not null) wo.Vendor = vendor;
            if (req.LaborCost.HasValue) wo.LaborCost = req.LaborCost;
            if (req.PartsCost.HasValue) wo.PartsCost = req.PartsCost;
            if (req.Currency is not null) wo.CurrencyLookupId = currencyId;
            if (req.Notes is not null) wo.Notes = OptionalText(req.Notes);

            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);

        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- tareas

    public async Task<WorkOrderDetailDto> AddTaskAsync(Guid publicId, MaintenanceTaskRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var description = TaskDescription(req.Description, required: true, errors);
        ValidateCost(req.PartCost, "partCost", errors);
        ValidateCost(req.LaborCost, "laborCost", errors);
        if (errors.Count > 0) throw new ValidationException(errors);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var wo = await LoadForWriteAsync(publicId, ct2);
            await statuses.EnsureAllowedAsync(EntityTypes.WorkOrder, wo.StatusCodeId, Capabilities.EditWorkOrder, ct2);
            var tasks = await ActiveTasksTrackedAsync(wo.WorkOrderId, ct2);

            var task = new MaintenanceTask
            {
                WorkOrderId = wo.WorkOrderId, Description = description!, PartCost = req.PartCost, LaborCost = req.LaborCost,
                IsCompleted = req.IsCompleted ?? false, IsActive = true,
            };
            db.MaintenanceTasks.Add(task);
            tasks.Add(task);

            RecalculateCosts(wo, tasks);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);

        return await GetAsync(publicId, ct);
    }

    public async Task<WorkOrderDetailDto> UpdateTaskAsync(Guid publicId, int taskId, MaintenanceTaskRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var description = TaskDescription(req.Description, required: false, errors);
        ValidateCost(req.PartCost, "partCost", errors);
        ValidateCost(req.LaborCost, "laborCost", errors);
        if (errors.Count > 0) throw new ValidationException(errors);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var wo = await LoadForWriteAsync(publicId, ct2);
            await statuses.EnsureAllowedAsync(EntityTypes.WorkOrder, wo.StatusCodeId, Capabilities.EditWorkOrder, ct2);
            var tasks = await ActiveTasksTrackedAsync(wo.WorkOrderId, ct2);
            // El id tiene que ser de una tarea activa de ESTA OT (BOLA por id hijo): otra OT u otro tenant → 404.
            var task = tasks.FirstOrDefault(t => t.MaintenanceTaskId == taskId) ?? throw new NotFoundException("Tarea", feminine: true);

            if (description is not null) task.Description = description;
            if (req.PartCost.HasValue) task.PartCost = req.PartCost;
            if (req.LaborCost.HasValue) task.LaborCost = req.LaborCost;
            if (req.IsCompleted.HasValue) task.IsCompleted = req.IsCompleted.Value;

            RecalculateCosts(wo, tasks);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);

        return await GetAsync(publicId, ct);
    }

    /// <summary>Quitar una tarea = IsActive 0 (nunca DELETE); deja de sumar en los costos y de contar para el cierre.</summary>
    public async Task<WorkOrderDetailDto> DeactivateTaskAsync(Guid publicId, int taskId, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var wo = await LoadForWriteAsync(publicId, ct2);
            await statuses.EnsureAllowedAsync(EntityTypes.WorkOrder, wo.StatusCodeId, Capabilities.EditWorkOrder, ct2);
            var tasks = await ActiveTasksTrackedAsync(wo.WorkOrderId, ct2);
            var task = tasks.FirstOrDefault(t => t.MaintenanceTaskId == taskId);
            if (task is null)
            {
                // Ya quitada (idempotente) si es de esta OT; si no es de esta OT, 404 sin oráculo.
                var inactiveHere = await db.MaintenanceTasks.AnyAsync(t => t.MaintenanceTaskId == taskId && t.WorkOrderId == wo.WorkOrderId, ct2);
                if (!inactiveHere) throw new NotFoundException("Tarea", feminine: true);
                return;
            }

            task.IsActive = false;
            tasks.Remove(task);
            RecalculateCosts(wo, tasks);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);

        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- estatus

    /// <summary>
    /// Cambio de estatus vía StatusService (dentro de una transacción: el efecto bloquea la fila del vehículo).
    /// Para CLOSED fija la fecha de cierre (hoy por defecto, nunca futura) y la lectura de odómetro, y aplica
    /// MaintenanceDue.ValidateClose ANTES de transicionar. IN_PROGRESS y CANCELLED van directo; OPEN → CLOSED también
    /// (terminal fuera de orden, lo permite StatusService).
    /// </summary>
    public async Task<WorkOrderDetailDto> TransitionStatusAsync(Guid publicId, WorkOrderStatusRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ToCode)) throw new ValidationException("toCode", ToCodeRequiredMessage);
        var toCode = req.ToCode.Trim().ToUpperInvariant();
        var closing = toCode == WorkOrderStatuses.Closed;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (closing)
        {
            var errors = new Dictionary<string, string[]>();
            if (req.CompletedDate is DateOnly cd && cd > today) errors["completedDate"] = new[] { CompletedDateFutureMessage };
            ValidateOdometer(req.OdometerKm, "odometerKm", errors);
            if (errors.Count > 0) throw new ValidationException(errors);
        }

        await db.RunInTransactionAsync(async ct2 =>
        {
            var wo = await LoadForWriteAsync(publicId, ct2);
            db.ApplyRowVersion(wo, req.RowVersion);

            if (closing)
            {
                var tasks = await ActiveTasksTrackedAsync(wo.WorkOrderId, ct2);
                string? trigger = null;
                if (wo.MaintenanceScheduleId is int sid)
                {
                    var triggerId = await db.MaintenanceSchedules.AsNoTracking().Where(s => s.MaintenanceScheduleId == sid)
                        .Select(s => (int?)s.TriggerLookupId).FirstOrDefaultAsync(ct2);
                    if (triggerId is int tid) trigger = (await lookups.GetAsync(tid, ct2))?.InternalCode;
                }
                var odometer = req.OdometerKm ?? wo.OdometerKm;
                var closeError = MaintenanceDue.ValidateClose(tasks.Count(t => !t.IsCompleted), trigger, odometer);
                if (closeError is { } e)
                {
                    if (e.Status == 422) throw new StatusRuleException(e.Message);
                    throw new ValidationException("odometerKm", e.Message);
                }
                // Se fijan en la entidad tracked ANTES de transicionar: WorkOrderStatusEffect los lee para el vehículo y el programa.
                wo.CompletedDate = req.CompletedDate ?? today;
                wo.OdometerKm = odometer;
            }

            var to = await statuses.TransitionAsync(StatusDomains.WorkOrderStatus, EntityTypes.WorkOrder, wo.WorkOrderId, wo.StatusCodeId, toCode, req.Comment, ct2);
            wo.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);

        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>OT tracked del tenant por PublicId (404 'Orden de trabajo no encontrada.').</summary>
    private async Task<MaintenanceWorkOrder> LoadForWriteAsync(Guid publicId, CancellationToken ct)
        => await db.MaintenanceWorkOrders.FirstOrDefaultAsync(w => w.PublicId == publicId, ct)
           ?? throw new NotFoundException("Orden de trabajo", feminine: true);

    /// <summary>Tareas activas de la OT, tracked (MaintenanceTask no lleva TenantId: la OT ya se resolvió bajo el filtro).</summary>
    private async Task<List<MaintenanceTask>> ActiveTasksTrackedAsync(int workOrderId, CancellationToken ct)
        => await db.MaintenanceTasks.Where(t => t.WorkOrderId == workOrderId && t.IsActive).OrderBy(t => t.MaintenanceTaskId).ToListAsync(ct);

    /// <summary>
    /// Con tareas activas: LaborCost = Σ LaborCost y PartsCost = Σ PartCost. Sin tareas activas, el encabezado queda vacío
    /// (se vuelve a capturar directo). La suma también respeta DECIMAL(18,4).
    /// </summary>
    private static void RecalculateCosts(MaintenanceWorkOrder wo, IReadOnlyCollection<MaintenanceTask> activeTasks)
    {
        if (activeTasks.Count == 0)
        {
            wo.LaborCost = null;
            wo.PartsCost = null;
            return;
        }
        var labor = activeTasks.Sum(t => t.LaborCost ?? 0m);
        var parts = activeTasks.Sum(t => t.PartCost ?? 0m);
        if (FleetRules.DecimalError(labor, 18, 4) is string le) throw new ValidationException("laborCost", le);
        if (FleetRules.DecimalError(parts, 18, 4) is string pe) throw new ValidationException("partCost", pe);
        wo.LaborCost = labor;
        wo.PartsCost = parts;
    }

    private static decimal Total(decimal? labor, decimal? parts) => (labor ?? 0m) + (parts ?? 0m);

    private static MaintenanceTaskDto ToTaskDto(MaintenanceTask t)
        => new(t.MaintenanceTaskId, t.Description, t.PartCost, t.LaborCost, t.IsCompleted, t.IsActive);

    private static void ValidateCost(decimal? value, string field, IDictionary<string, string[]> errors)
    {
        if (value is null) return;
        if (value < 0) { errors[field] = new[] { NegativeCostMessage }; return; }
        if (FleetRules.DecimalError(value, 18, 4) is string e) errors[field] = new[] { e };
    }

    private static void ValidateOdometer(decimal? value, string field, IDictionary<string, string[]> errors)
    {
        if (value is null) return;
        if (value < 0) { errors[field] = new[] { NegativeOdometerMessage }; return; }
        if (FleetRules.DecimalError(value, 12, 1) is string e) errors[field] = new[] { e };
    }

    /// <summary>null = sin valor/sin cambio; en alta obligatoria. Recortada; más de 250 → error.</summary>
    private static string? TaskDescription(string? raw, bool required, IDictionary<string, string[]> errors)
    {
        if (raw is null)
        {
            if (required) errors["description"] = new[] { TaskDescriptionRequiredMessage };
            return null;
        }
        var d = raw.Trim();
        if (d.Length == 0) { errors["description"] = new[] { TaskDescriptionRequiredMessage }; return null; }
        if (d.Length > MaintenanceDue.TaskDescriptionMaxLength) { errors["description"] = new[] { TaskDescriptionTooLongMessage }; return null; }
        return d;
    }

    /// <summary>null = sin valor/sin cambio; "" = NULL; otro = recortado (error si excede el largo de la columna).</summary>
    private static string? Text(string? value, string field, int max, string tooLongMessage, IDictionary<string, string[]> errors)
    {
        if (value is null) return null;
        var v = value.Trim();
        if (v.Length == 0) return null;
        if (v.Length > max) errors[field] = new[] { tooLongMessage };
        return v;
    }

    private static string? OptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Moneda por código. null o "" → null; desconocida → 400 en el campo currency.</summary>
    private async Task<int?> CurrencyIdAsync(string? code, IDictionary<string, string[]> errors, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var id = await lookups.TryGetIdAsync(LookupDomains.Currency, code.Trim().ToUpperInvariant(), ct);
        if (id is null) errors["currency"] = new[] { $"Moneda desconocida: '{code.Trim()}'." };
        return id;
    }

    private static List<string> SplitCodes(string[]? codes)
        => codes is null
            ? new List<string>()
            : codes.Where(c => c is not null)
                .SelectMany(c => c.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(c => c.ToUpperInvariant()).Distinct().ToList();

    /// <summary>Estatus del dominio WorkOrderStatus con la etiqueta y el color personalizados del tenant si existen.</summary>
    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Include(s => s.StageKind).Where(s => s.Entity == StatusDomains.WorkOrderStatus).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, StatusCodeOverride>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        return codes.ToDictionary(c => c.StatusCodeId, c =>
        {
            var o = overrides.GetValueOrDefault(c.StatusCodeId);
            return new StatusInfo(c.InternalCode,
                MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, o?.CustomLabelJson), tenant.Lang),
                o?.CustomColorHex ?? c.ColorHex,
                c.StageKind?.InternalCode == StageKinds.Terminal);
        });
    }

    private string? Label(LookupCode? l) => l is null ? null : MultilingualText.Resolve(l.LabelJson, tenant.Lang);
}
