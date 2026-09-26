using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 4 (P7): viajes pagados al chofer (DriverTrip), insumo de la liquidación del Lote 9.
/// - Monto congelado al crear (DriverTripRules.Freeze) con la tarifa por viaje vigente en TripDate (IDriverRateResolver); sin
///   tarifa nace en $0 con RateMissing (R15, CK_DriverTrip_Rate). Cambiar la tarifa después no toca los viajes existentes.
/// - Estatus propio DriverTripStatus vía StatusService: OPEN (inicial) → SETTLED (Lote 9) | CANCELLED. Al cancelar,
///   DriverTripStatusEffect deja IsActive = 0: UX_DriverTrip_Order garantiza un solo viaje vigente por orden.
/// - Todo se alcanza a través del chofer del tenant (ruta /drivers/{publicId}/trips, R31): un viaje de otro chofer u otro
///   tenant responde 404 'Viaje no encontrado.'. Chofer eliminado (terminal): solo se consulta (409).
/// - El viaje de una entrega especial no se cancela directo (409): se cancela la orden o se reasigna el chofer.
/// </summary>
public sealed class DriverTripService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, IDriverRateResolver rates)
{
    /// <summary>Choque en UX_DriverTrip_Order: otra asignación dejó un viaje vigente para la misma orden.</summary>
    public const string OrderTripTakenMessage = "La orden ya tiene un viaje vigente; recargue e intente de nuevo.";
    public const string DateRangeMessage = "La fecha inicial no puede ser posterior a la final.";

    // ================================================================ uso interno (dentro de la transacción del llamador)

    /// <summary>
    /// Crea el viaje dentro de la transacción del llamador: congela la tarifa (la ya resuelta o la vigente en tripDate), lo
    /// guarda (SaveGuardedAsync: un choque en UX_DriverTrip_Order responde 409) y registra su nacimiento en OPEN.
    /// El llamador guarda el historial con su propio SaveChanges.
    /// </summary>
    public async Task<DriverTrip> CreateTrackedAsync(int driverId, int specialServiceTypeId, DateOnly tripDate, int? transportOrderId,
        string? notes, DriverRateResolution? preResolved = null, CancellationToken ct = default)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var resolution = preResolved ?? await rates.TripRateAsync(driverId, specialServiceTypeId, tripDate, ct);
        var frozen = resolution.Missing
            ? DriverTripRules.Freeze(null, null)
            : DriverTripRules.Freeze(resolution.Amount, resolution.RateId);

        var initial = await statuses.GetInitialAsync(StatusDomains.DriverTripStatus, ct);
        var trip = new DriverTrip
        {
            TenantId = tenantId,
            DriverId = driverId,
            SpecialServiceTypeId = specialServiceTypeId,
            DriverTripRateId = frozen.RateId,
            TransportOrderId = transportOrderId,
            TripDate = tripDate,
            Amount = frozen.Amount,
            RateMissing = frozen.RateMissing,
            StatusCodeId = initial.StatusCodeId,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            IsActive = true,
        };
        db.DriverTrips.Add(trip);
        await db.SaveGuardedAsync(OrderTripTakenMessage, ct);

        var born = await statuses.TransitionAsync(StatusDomains.DriverTripStatus, EntityTypes.DriverTrip, trip.DriverTripId, null,
            initial.InternalCode, null, ct);
        trip.StatusCodeId = born.StatusCodeId;
        return trip;
    }

    /// <summary>
    /// Cancela un viaje tracked (reasignación, orden cancelada por código propio) y GUARDA de inmediato: el viaje queda
    /// IsActive = 0 (DriverTripStatusEffect) antes de que el llamador inserte otro para la misma orden (UX_DriverTrip_Order).
    /// SETTLED/CANCELLED son terminales: StatusService responde 422.
    /// </summary>
    public async Task CancelTrackedAsync(DriverTrip trip, string? comment, CancellationToken ct)
    {
        var to = await statuses.TransitionAsync(StatusDomains.DriverTripStatus, EntityTypes.DriverTrip, trip.DriverTripId,
            trip.StatusCodeId, DriverTripStatuses.Cancelled, comment, ct);
        trip.StatusCodeId = to.StatusCodeId;
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
    }

    // ================================================================ alta manual

    public async Task<DriverTripDto> CreateAsync(Guid driverPublicId, DriverTripCreateRequest req, CancellationToken ct)
    {
        if (req is null || req.SpecialServiceTypeId is not int typeId) throw new ValidationException("specialServiceTypeId", DriverTripRules.TypeRequiredMessage);
        var tripDate = req.TripDate ?? Today();
        if (DriverTripRules.ValidateTripDate(tripDate, Today()) is string dateError) throw new ValidationException("tripDate", dateError);
        if (DriverTripRules.ValidateNotes(req.Notes) is string notesError) throw new ValidationException("notes", notesError);

        var driver = await db.ResolveDriverAsync(driverPublicId, false, ct); // 404 'Chofer no encontrado.'
        if (await db.IsTerminalAsync(driver.StatusCodeId, ct)) throw new ConflictException(FleetQueries.DriverRetiredMessage);
        await EnsureTripTypeAsync(typeId, ct);

        var tripId = await db.RunInTransactionAsync(async ct2 =>
        {
            // Se vuelve a leer dentro de la transacción: una baja definitiva concurrente no deja pasar el alta.
            var current = await db.ResolveDriverAsync(driverPublicId, false, ct2);
            if (await db.IsTerminalAsync(current.StatusCodeId, ct2)) throw new ConflictException(FleetQueries.DriverRetiredMessage);
            var trip = await CreateTrackedAsync(current.DriverId, typeId, tripDate, null, req.Notes, null, ct2);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            return trip.DriverTripId;
        }, ct);

        return (await ToDtosAsync(driver.PublicId, driver.EmployeeCode, db.DriverTrips.AsNoTracking().Where(t => t.DriverTripId == tripId), ct)).Single();
    }

    // ================================================================ listado

    /// <summary>Viajes del chofer: por defecto solo vigentes (IsActive = 1); includeCancelled agrega los cancelados. from/to inclusivos sobre TripDate.</summary>
    public async Task<IReadOnlyList<DriverTripDto>> ListAsync(Guid driverPublicId, DriverTripListQuery q, CancellationToken ct)
    {
        q ??= new DriverTripListQuery();
        if (q.From is DateOnly f && q.To is DateOnly t0 && f > t0) throw new ValidationException("from", DateRangeMessage);

        var driver = await db.ResolveDriverAsync(driverPublicId, false, ct);
        var driverId = driver.DriverId;
        var query = db.DriverTrips.AsNoTracking().Where(t => t.DriverId == driverId);

        var includeCancelled = q.IncludeCancelled;
        var statusCodes = SplitCodes(q.Status);
        if (statusCodes.Count > 0)
        {
            var known = await db.StatusCodes.AsNoTracking()
                .Where(s => s.Entity == StatusDomains.DriverTripStatus)
                .Select(s => new { s.StatusCodeId, s.InternalCode })
                .ToListAsync(ct);
            var ids = new List<int>();
            foreach (var code in statusCodes)
            {
                var match = known.FirstOrDefault(s => string.Equals(s.InternalCode, code, StringComparison.OrdinalIgnoreCase))
                            ?? throw new ValidationException("status", $"Estatus de viaje desconocido: '{code}'.");
                ids.Add(match.StatusCodeId);
            }
            query = query.Where(t => ids.Contains(t.StatusCodeId));
            // Pedir CANCELLED explícitamente equivale a incluir los cancelados (quedan con IsActive = 0).
            if (statusCodes.Contains(DriverTripStatuses.Cancelled, StringComparer.OrdinalIgnoreCase)) includeCancelled = true;
        }
        if (!includeCancelled) query = query.Where(t => t.IsActive);
        if (q.From is DateOnly from) query = query.Where(t => t.TripDate >= from);
        if (q.To is DateOnly to) query = query.Where(t => t.TripDate <= to);

        query = query.OrderByDescending(t => t.TripDate).ThenByDescending(t => t.DriverTripId);
        return await ToDtosAsync(driver.PublicId, driver.EmployeeCode, query, ct);
    }

    // ================================================================ cancelar

    public async Task<DriverTripDto> CancelAsync(Guid driverPublicId, Guid tripPublicId, string? comment, CancellationToken ct)
    {
        var driver = await db.ResolveDriverAsync(driverPublicId, false, ct);
        if (await db.IsTerminalAsync(driver.StatusCodeId, ct)) throw new ConflictException(FleetQueries.DriverRetiredMessage);
        var driverId = driver.DriverId;

        await db.RunInTransactionAsync(async ct2 =>
        {
            // El viaje se busca SOLO entre los del chofer de la ruta (BOLA por id hijo: otro chofer u otro tenant → 404).
            var trip = await db.DriverTrips.FirstOrDefaultAsync(t => t.PublicId == tripPublicId && t.DriverId == driverId, ct2)
                       ?? throw new NotFoundException(DriverTripRules.TripNotFoundLabel);
            // El de una entrega especial vigente se cancela con la orden o se reemplaza al reasignar; uno ya terminal cae en el 422.
            if (trip.TransportOrderId is not null && !await db.IsTerminalAsync(trip.StatusCodeId, ct2))
                throw new ConflictException(DriverTripRules.OrderLinkedMessage);
            await CancelTrackedAsync(trip, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), ct2);
        }, ct);

        return (await ToDtosAsync(driver.PublicId, driver.EmployeeCode,
            db.DriverTrips.AsNoTracking().Where(t => t.PublicId == tripPublicId && t.DriverId == driverId), ct)).Single();
    }

    // ================================================================ helpers

    /// <summary>Tipo de viaje = tipo de servicio especial del tenant (R21): 404 si no existe, 400 si está inactivo.</summary>
    private async Task EnsureTripTypeAsync(int typeId, CancellationToken ct)
    {
        var type = await db.SpecialServiceTypes.AsNoTracking()
                       .Where(s => s.SpecialServiceTypeId == typeId)
                       .Select(s => new { s.IsActive })
                       .FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(DriverTripRules.TypeNotFoundLabel);
        if (!type.IsActive) throw new ValidationException("specialServiceTypeId", DriverTripRules.TypeInactiveMessage);
    }

    private async Task<IReadOnlyList<DriverTripDto>> ToDtosAsync(Guid driverPublicId, string driverCode, IQueryable<DriverTrip> query, CancellationToken ct)
    {
        var rows = await query.ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<DriverTripDto>();

        var typeIds = rows.Select(r => r.SpecialServiceTypeId).Distinct().ToList();
        var typeNames = await db.SpecialServiceTypes.AsNoTracking()
            .Where(s => typeIds.Contains(s.SpecialServiceTypeId))
            .ToDictionaryAsync(s => s.SpecialServiceTypeId, s => s.Name, ct);

        var orderIds = rows.Where(r => r.TransportOrderId.HasValue).Select(r => r.TransportOrderId!.Value).Distinct().ToList();
        var orders = orderIds.Count == 0
            ? new Dictionary<int, (Guid PublicId, string Number)>()
            : (await db.TransportOrders.AsNoTracking()
                    .Where(o => orderIds.Contains(o.TransportOrderId))
                    .Select(o => new { o.TransportOrderId, o.PublicId, o.OrderNumber })
                    .ToListAsync(ct))
                .ToDictionary(o => o.TransportOrderId, o => (o.PublicId, o.OrderNumber));

        var statusLabels = await StatusLabelsAsync(ct);

        return rows.Select(t =>
        {
            var status = statusLabels.TryGetValue(t.StatusCodeId, out var st) ? st : (Code: string.Empty, Label: string.Empty);
            (Guid PublicId, string Number)? order = t.TransportOrderId is int oid && orders.TryGetValue(oid, out var o) ? o : null;
            return new DriverTripDto(
                t.DriverTripId, t.PublicId, driverPublicId, driverCode,
                t.SpecialServiceTypeId, typeNames.GetValueOrDefault(t.SpecialServiceTypeId) ?? string.Empty,
                t.TripDate, t.Amount, t.RateMissing, DriverTripRules.NoteFor(t.RateMissing),
                order?.PublicId, order?.Number,
                status.Code, status.Label,
                t.Notes, t.IsActive, t.CreatedAtUtc);
        }).ToList();
    }

    /// <summary>Código y etiqueta (con la personalizada del tenant) de cada estatus de DriverTripStatus.</summary>
    private async Task<Dictionary<int, (string Code, string Label)>> StatusLabelsAsync(CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.DriverTripStatus).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var custom = tenant.TenantId is null || ids.Count == 0
            ? new Dictionary<int, string?>()
            : await db.StatusCodeOverrides.AsNoTracking()
                .Where(o => ids.Contains(o.StatusCodeId) && o.CustomLabelJson != null)
                .ToDictionaryAsync(o => o.StatusCodeId, o => o.CustomLabelJson, ct);
        return codes.ToDictionary(c => c.StatusCodeId,
            c => (c.InternalCode, MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, custom.GetValueOrDefault(c.StatusCodeId)), tenant.Lang)));
    }

    /// <summary>Filtro multi-valor: ?status=OPEN&amp;status=SETTLED o ?status=OPEN,SETTLED (sin vacíos, en mayúsculas, sin repetir).</summary>
    private static List<string> SplitCodes(string[]? values)
        => values is null
            ? new List<string>()
            : values.SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(v => v.ToUpperInvariant())
                .Distinct()
                .ToList();

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
