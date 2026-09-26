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
/// Lote 4 (P6) — Tarifas del chofer (detalle 'Tarifas' de la ficha 'Choferes y tarifas', módulo 11A).
/// - El chofer es el agregado raíz: las tres tablas (entrega, intento, viaje) cuelgan de Driver y toda fila se alcanza
///   a través del chofer del tenant (FleetQueries.ResolveDriverAsync + DriverId), nunca por id suelto. Un id que no es de
///   ese chofer (otro chofer u otro tenant) es 404 'Tarifa no encontrada.' (BOLA por id hijo). Ninguna solicitud trae el
///   chofer en el cuerpo: sale de la ruta (R31).
/// - Efectivo-fechadas (principio #8): editar = cerrar la fila abierta y abrir otra; quitar = cerrar. Nunca se sobrescribe
///   un monto ni se hace DELETE. Primero se cierra y se guarda, luego se inserta (UQ_*_Open admite una sola fila abierta).
///   Ninguna fecha nueva va al pasado (RateService.PastDateMessage).
/// - Chofer eliminado (estatus terminal): solo se consulta; toda escritura es 409 FleetQueries.DriverRetiredMessage.
///   Un chofer inactivo (IsActive = 0) sí admite tarifas: se puede reactivar.
/// - Rate &gt;= 0 y precisión DECIMAL(18,4) validadas antes de guardar (nunca un 500 por desbordamiento).
/// </summary>
public sealed class DriverRateService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    private const string RateWhat = "Tarifa";
    private const string ClosedMessage = "La tarifa ya está cerrada; agregue una nueva si necesita volver a pagarla.";
    private const string DeliveryKeysMessage = "El servicio y el paquete se fijan al crear la tarifa; quite la fila y cree una nueva.";
    private const string TripTypeKeyMessage = "El tipo de viaje se fija al crear la tarifa; quite la fila y agregue una con el tipo correcto.";
    private const string NoTripTypesMessage = "Sin servicios especiales: agréguelos en Clientes y contratos antes de configurar tarifas por viaje.";
    private const string InactiveTripTypeMessage = "El tipo de servicio especial está inactivo; reactívelo o elija otro.";

    private static readonly string[] DeliveryImmutableKeys =
        { "serviceType", "packageType", "serviceTypeCode", "packageTypeCode", "serviceTypeLookupId", "packageTypeLookupId" };
    private static readonly string[] TripImmutableKeys =
        { "specialServiceTypeId", "tripType", "tripTypeId", "typeId" };

    // ---------------------------------------------------------------- consulta

    /// <summary>
    /// Tarifas del chofer vigentes en asOf (hoy por defecto); includeHistory agrega las filas cerradas o futuras.
    /// AttemptRates trae una entrada por nivel 1..AttemptLevels (Rate null = sin tarifa) más los niveles superiores con filas.
    /// </summary>
    public async Task<DriverRatesDto> GetAsync(Guid driverPublicId, DateOnly? asOf, bool includeHistory, CancellationToken ct)
    {
        var date = asOf ?? Today();
        var driver = await db.ResolveDriverAsync(driverPublicId, false, ct);
        var readOnly = await db.IsTerminalAsync(driver.StatusCodeId, ct);
        var policy = await DriverPayPolicyService.SnapshotAsync(db, lookups, ct);
        var driverId = driver.DriverId;

        // Entregas
        var deliveryRows = await db.DriverDeliveryRates.AsNoTracking().Where(r => r.DriverId == driverId && r.IsActive).ToListAsync(ct);
        var delivery = new List<DriverDeliveryRateDto>();
        foreach (var r in deliveryRows.Where(r => includeHistory || EffectiveDated.IsCurrentOn(r, date)))
            delivery.Add(await DeliveryDtoAsync(r, date, ct));
        delivery = delivery.OrderBy(d => d.ServiceType).ThenBy(d => d.PackageType).ThenBy(d => d.EffectiveFrom).ThenBy(d => d.Id).ToList();

        // Intentos: una entrada por nivel configurado (placeholder 'sin tarifa' si no hay fila vigente)
        var attemptRows = await db.DriverAttemptRates.AsNoTracking().Where(r => r.DriverId == driverId && r.IsActive).ToListAsync(ct);
        var byLevel = attemptRows.GroupBy(r => r.AttemptNumber).ToDictionary(g => g.Key, g => g.ToList());
        var maxLevel = Math.Max(policy.AttemptLevels, byLevel.Count == 0 ? 0 : byLevel.Keys.Max());
        var attempts = new List<DriverAttemptRateDto>();
        for (var level = 1; level <= maxLevel; level++)
        {
            var rows = byLevel.GetValueOrDefault(level) ?? new List<DriverAttemptRate>();
            var current = DriverPayoutRules.PickCurrent(rows, date);
            if (includeHistory)
                attempts.AddRange(rows.OrderBy(r => r.EffectiveFrom).ThenBy(r => r.DriverAttemptRateId).Select(r => AttemptDto(r, date)));
            else if (current is not null)
                attempts.Add(AttemptDto(current, date));
            if (current is null && level <= policy.AttemptLevels)
                attempts.Add(new DriverAttemptRateDto(level, null, null, null, null, false));
        }

        // Viajes
        var tripRows = await db.DriverTripRates.AsNoTracking().Where(r => r.DriverId == driverId && r.IsActive).ToListAsync(ct);
        var visibleTrips = tripRows.Where(r => includeHistory || EffectiveDated.IsCurrentOn(r, date)).ToList();
        var typeIds = visibleTrips.Select(r => r.SpecialServiceTypeId).Distinct().ToList();
        var types = await db.SpecialServiceTypes.AsNoTracking()
            .Where(t => typeIds.Contains(t.SpecialServiceTypeId))
            .ToDictionaryAsync(t => t.SpecialServiceTypeId, ct);
        var trips = visibleTrips
            .Select(r =>
            {
                var t = types.GetValueOrDefault(r.SpecialServiceTypeId);
                return TripDto(r, t?.Name ?? "", t?.IsActive ?? false, date);
            })
            .OrderBy(t => t.TripType).ThenBy(t => t.EffectiveFrom).ThenBy(t => t.Id)
            .ToList();

        return new DriverRatesDto(driver.PublicId, driver.EmployeeCode, driver.FullName, readOnly, date, policy.AttemptLevels,
            policy.FormulaCode, delivery, attempts, trips);
    }

    // ---------------------------------------------------------------- entregas

    /// <summary>Alta de tarifa por entrega (servicio + tipo de paquete exactos). 409 si ya hay una viva para el par.</summary>
    public async Task<DriverDeliveryRateDto> AddDeliveryRateAsync(Guid driverPublicId, DriverDeliveryRateCreateRequest req, CancellationToken ct)
    {
        var tenantId = RequireTenantId();
        if (string.IsNullOrWhiteSpace(req.ServiceType) || string.IsNullOrWhiteSpace(req.PackageType))
            throw new ValidationException(string.IsNullOrWhiteSpace(req.ServiceType) ? "serviceType" : "packageType",
                "Indique el servicio y el tipo de paquete de la tarifa.");
        var serviceTypeId = await LookupOrFailAsync(LookupDomains.ServiceType, req.ServiceType, "serviceType", "Tipo de servicio desconocido", ct);
        var packageTypeId = await LookupOrFailAsync(LookupDomains.PackageType, req.PackageType, "packageType", "Tipo de paquete desconocido", ct);
        ValidateRate(req.Rate);
        var effectiveFrom = req.EffectiveFrom ?? Today();
        EnsureNotPast(effectiveFrom, "effectiveFrom");
        var duplicate = await DeliveryDuplicateMessageAsync(serviceTypeId, packageTypeId, ct);

        var row = await db.RunInTransactionAsync(async ct2 =>
        {
            var driver = await LoadWritableDriverAsync(driverPublicId, ct2);
            var driverId = driver.DriverId;
            // Guarda por intervalo: la fila nueva nace abierta y choca con toda fila que no haya terminado antes de effectiveFrom.
            // UQ_DriverDeliveryRate_Open (solo abiertas) es la segunda barrera.
            if (await db.DriverDeliveryRates.AnyAsync(r => r.DriverId == driverId && r.IsActive
                                                           && r.ServiceTypeLookupId == serviceTypeId && r.PackageTypeLookupId == packageTypeId
                                                           && (r.EffectiveTo == null || r.EffectiveTo > effectiveFrom), ct2))
                throw new ConflictException(duplicate);

            var next = new DriverDeliveryRate
            {
                TenantId = tenantId, DriverId = driverId,
                ServiceTypeLookupId = serviceTypeId, PackageTypeLookupId = packageTypeId,
                Rate = req.Rate, EffectiveFrom = effectiveFrom, EffectiveTo = null, IsActive = true,
                CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
            };
            db.DriverDeliveryRates.Add(next);
            await db.SaveGuardedAsync(duplicate, ct2);
            return next;
        }, ct);

        return await DeliveryDtoAsync(row, Today(), ct);
    }

    /// <summary>Nueva versión de la tarifa por entrega: cierra la fila abierta en effectiveFrom y abre otra (devuelve la nueva).</summary>
    public async Task<DriverDeliveryRateDto> UpdateDeliveryRateAsync(Guid driverPublicId, int id, DriverRateUpdateRequest req, CancellationToken ct)
    {
        var tenantId = RequireTenantId();
        RejectKeys(req.Extra, DeliveryImmutableKeys, "serviceType", DeliveryKeysMessage);
        ValidateRate(req.Rate);
        var newFrom = req.EffectiveFrom ?? Today();
        EnsureNotPast(newFrom, "effectiveFrom");

        var row = await db.RunInTransactionAsync(async ct2 =>
        {
            var driver = await LoadWritableDriverAsync(driverPublicId, ct2);
            var current = await db.DriverDeliveryRates.FirstOrDefaultAsync(r => r.DriverDeliveryRateId == id && r.DriverId == driver.DriverId && r.IsActive, ct2)
                          ?? throw new NotFoundException(RateWhat, null, true);
            if (current.EffectiveTo is not null) throw new ConflictException(ClosedMessage);
            var duplicate = await DeliveryDuplicateMessageAsync(current.ServiceTypeLookupId, current.PackageTypeLookupId, ct2);

            NewVersion(current, newFrom, "effectiveFrom");
            await db.SaveGuardedAsync(duplicate, ct2);

            var next = new DriverDeliveryRate
            {
                TenantId = tenantId, DriverId = current.DriverId,
                ServiceTypeLookupId = current.ServiceTypeLookupId, PackageTypeLookupId = current.PackageTypeLookupId,
                Rate = req.Rate, EffectiveFrom = newFrom, EffectiveTo = null, IsActive = true,
                CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
            };
            db.DriverDeliveryRates.Add(next);
            await db.SaveGuardedAsync(duplicate, ct2);
            return next;
        }, ct);

        return await DeliveryDtoAsync(row, Today(), ct);
    }

    /// <summary>Quitar conservando historial: cierra la fila en effectiveTo (hoy por defecto, exclusivo).</summary>
    public async Task<DriverDeliveryRateDto> CloseDeliveryRateAsync(Guid driverPublicId, int id, DriverRateCloseRequest? req, CancellationToken ct)
    {
        var to = req?.EffectiveTo ?? Today();
        EnsureNotPast(to, "effectiveTo");

        var row = await db.RunInTransactionAsync(async ct2 =>
        {
            var driver = await LoadWritableDriverAsync(driverPublicId, ct2);
            var current = await db.DriverDeliveryRates.FirstOrDefaultAsync(r => r.DriverDeliveryRateId == id && r.DriverId == driver.DriverId && r.IsActive, ct2)
                          ?? throw new NotFoundException(RateWhat, null, true);
            if (current.EffectiveTo is not null) throw new ConflictException(ClosedMessage);
            CloseOn(current, to, "effectiveTo");
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            return current;
        }, ct);

        return await DeliveryDtoAsync(row, Today(), ct);
    }

    // ---------------------------------------------------------------- intentos

    /// <summary>
    /// Fija la tarifa del intento n (1..AttemptLevels de la compañía): si hay fila abierta la versiona (cierra y abre);
    /// si no, la crea.
    /// </summary>
    public async Task<DriverAttemptRateDto> SetAttemptRateAsync(Guid driverPublicId, int attemptNumber, DriverRateUpdateRequest req, CancellationToken ct)
    {
        var tenantId = RequireTenantId();
        ValidateRate(req.Rate);
        var newFrom = req.EffectiveFrom ?? Today();
        EnsureNotPast(newFrom, "effectiveFrom");
        var duplicate = $"El chofer ya tiene una tarifa vigente para el intento {attemptNumber}; edite esa tarifa o ciérrela antes de agregar otra.";

        var row = await db.RunInTransactionAsync(async ct2 =>
        {
            var driver = await LoadWritableDriverAsync(driverPublicId, ct2);
            var policy = await DriverPayPolicyService.SnapshotAsync(db, lookups, ct2);
            var levelError = DriverPayoutRules.ValidateAttemptNumber(attemptNumber, policy.AttemptLevels);
            if (levelError is not null) throw new ValidationException("attemptNumber", levelError);

            var driverId = driver.DriverId;
            var open = await db.DriverAttemptRates.FirstOrDefaultAsync(r => r.DriverId == driverId && r.AttemptNumber == attemptNumber
                                                                            && r.IsActive && r.EffectiveTo == null, ct2);
            if (open is not null)
            {
                NewVersion(open, newFrom, "effectiveFrom");
                await db.SaveGuardedAsync(duplicate, ct2);
            }
            else
            {
                // Una fila cerrada a futuro sigue viva hasta su cierre: la nueva no puede empezar antes.
                var live = await db.DriverAttemptRates.AsNoTracking()
                    .Where(r => r.DriverId == driverId && r.AttemptNumber == attemptNumber && r.IsActive && r.EffectiveTo > newFrom)
                    .OrderByDescending(r => r.EffectiveTo).FirstOrDefaultAsync(ct2);
                if (live is not null)
                    throw new ConflictException($"El intento {attemptNumber} tiene una tarifa vigente hasta el {live.EffectiveTo:yyyy-MM-dd}; la nueva debe empezar en esa fecha o después.");
            }

            var next = new DriverAttemptRate
            {
                TenantId = tenantId, DriverId = driverId, AttemptNumber = attemptNumber,
                Rate = req.Rate, EffectiveFrom = newFrom, EffectiveTo = null, IsActive = true,
                CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
            };
            db.DriverAttemptRates.Add(next);
            await db.SaveGuardedAsync(duplicate, ct2);
            return next;
        }, ct);

        return AttemptDto(row, Today());
    }

    /// <summary>Cierra la tarifa abierta del intento n (el nivel queda 'sin tarifa' desde effectiveTo).</summary>
    public async Task<DriverAttemptRateDto> CloseAttemptRateAsync(Guid driverPublicId, int attemptNumber, DriverRateCloseRequest? req, CancellationToken ct)
    {
        var to = req?.EffectiveTo ?? Today();
        EnsureNotPast(to, "effectiveTo");

        var row = await db.RunInTransactionAsync(async ct2 =>
        {
            var driver = await LoadWritableDriverAsync(driverPublicId, ct2);
            var open = await db.DriverAttemptRates.FirstOrDefaultAsync(r => r.DriverId == driver.DriverId && r.AttemptNumber == attemptNumber
                                                                            && r.IsActive && r.EffectiveTo == null, ct2)
                       ?? throw new NotFoundException(RateWhat, null, true);
            CloseOn(open, to, "effectiveTo");
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            return open;
        }, ct);

        return AttemptDto(row, Today());
    }

    // ---------------------------------------------------------------- viajes

    /// <summary>
    /// Selector 'tipo de viaje': los tipos activos del catálogo de servicios especiales del tenant (R21). Se proyecta a
    /// <see cref="DriverTripTypeDto"/> (solo id y nombre): el conteo de clientes que usan cada tipo es dato de contratos
    /// (contracts.read) y no se expone por la puerta de compensación (driverpay.view).
    /// </summary>
    public async Task<IReadOnlyList<DriverTripTypeDto>> GetTripTypesAsync(CancellationToken ct)
        => await db.SpecialServiceTypes.AsNoTracking().Where(t => t.IsActive)
            .OrderBy(t => t.Name).ThenBy(t => t.SpecialServiceTypeId)
            .Select(t => new DriverTripTypeDto(t.SpecialServiceTypeId, t.Name))
            .ToListAsync(ct);

    /// <summary>Alta de tarifa por viaje de un tipo del catálogo de servicios especiales. 409 si ya hay una viva de ese tipo.</summary>
    public async Task<DriverTripRateDto> AddTripRateAsync(Guid driverPublicId, DriverTripRateCreateRequest req, CancellationToken ct)
    {
        var tenantId = RequireTenantId();
        ValidateRate(req.Rate);
        var effectiveFrom = req.EffectiveFrom ?? Today();
        EnsureNotPast(effectiveFrom, "effectiveFrom");

        var (row, type) = await db.RunInTransactionAsync(async ct2 =>
        {
            var driver = await LoadWritableDriverAsync(driverPublicId, ct2);
            if (!await db.SpecialServiceTypes.AnyAsync(t => t.IsActive, ct2))
                throw new ValidationException("specialServiceTypeId", NoTripTypesMessage);
            if (req.SpecialServiceTypeId is not int typeId)
                throw new ValidationException("specialServiceTypeId", "El tipo de viaje es obligatorio.");
            var tripType = await db.SpecialServiceTypes.AsNoTracking().FirstOrDefaultAsync(t => t.SpecialServiceTypeId == typeId, ct2)
                           ?? throw new NotFoundException("Tipo de servicio especial");
            if (!tripType.IsActive) throw new ValidationException("specialServiceTypeId", InactiveTripTypeMessage);

            var duplicate = TripDuplicateMessage(tripType.Name);
            var driverId = driver.DriverId;
            if (await db.DriverTripRates.AnyAsync(r => r.DriverId == driverId && r.SpecialServiceTypeId == typeId && r.IsActive
                                                       && (r.EffectiveTo == null || r.EffectiveTo > effectiveFrom), ct2))
                throw new ConflictException(duplicate);

            var next = new DriverTripRate
            {
                TenantId = tenantId, DriverId = driverId, SpecialServiceTypeId = typeId,
                Rate = req.Rate, EffectiveFrom = effectiveFrom, EffectiveTo = null, IsActive = true,
                CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
            };
            db.DriverTripRates.Add(next);
            await db.SaveGuardedAsync(duplicate, ct2);
            return (next, tripType);
        }, ct);

        return TripDto(row, type.Name, type.IsActive, Today());
    }

    /// <summary>Nueva versión de la tarifa por viaje: cierra la fila abierta en effectiveFrom y abre otra. El tipo es inmutable.</summary>
    public async Task<DriverTripRateDto> UpdateTripRateAsync(Guid driverPublicId, int id, DriverRateUpdateRequest req, CancellationToken ct)
    {
        var tenantId = RequireTenantId();
        RejectKeys(req.Extra, TripImmutableKeys, "specialServiceTypeId", TripTypeKeyMessage);
        ValidateRate(req.Rate);
        var newFrom = req.EffectiveFrom ?? Today();
        EnsureNotPast(newFrom, "effectiveFrom");

        var (row, typeName, typeActive) = await db.RunInTransactionAsync(async ct2 =>
        {
            var driver = await LoadWritableDriverAsync(driverPublicId, ct2);
            var current = await db.DriverTripRates.FirstOrDefaultAsync(r => r.DriverTripRateId == id && r.DriverId == driver.DriverId && r.IsActive, ct2)
                          ?? throw new NotFoundException(RateWhat, null, true);
            if (current.EffectiveTo is not null) throw new ConflictException(ClosedMessage);
            var type = await db.SpecialServiceTypes.AsNoTracking().FirstOrDefaultAsync(t => t.SpecialServiceTypeId == current.SpecialServiceTypeId, ct2);
            var duplicate = TripDuplicateMessage(type?.Name ?? "");

            NewVersion(current, newFrom, "effectiveFrom");
            await db.SaveGuardedAsync(duplicate, ct2);

            var next = new DriverTripRate
            {
                TenantId = tenantId, DriverId = current.DriverId, SpecialServiceTypeId = current.SpecialServiceTypeId,
                Rate = req.Rate, EffectiveFrom = newFrom, EffectiveTo = null, IsActive = true,
                CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
            };
            db.DriverTripRates.Add(next);
            await db.SaveGuardedAsync(duplicate, ct2);
            return (next, type?.Name ?? "", type?.IsActive ?? false);
        }, ct);

        return TripDto(row, typeName, typeActive, Today());
    }

    /// <summary>Quitar conservando historial: cierra la tarifa por viaje en effectiveTo (hoy por defecto, exclusivo).</summary>
    public async Task<DriverTripRateDto> CloseTripRateAsync(Guid driverPublicId, int id, DriverRateCloseRequest? req, CancellationToken ct)
    {
        var to = req?.EffectiveTo ?? Today();
        EnsureNotPast(to, "effectiveTo");

        var (row, typeName, typeActive) = await db.RunInTransactionAsync(async ct2 =>
        {
            var driver = await LoadWritableDriverAsync(driverPublicId, ct2);
            var current = await db.DriverTripRates.FirstOrDefaultAsync(r => r.DriverTripRateId == id && r.DriverId == driver.DriverId && r.IsActive, ct2)
                          ?? throw new NotFoundException(RateWhat, null, true);
            if (current.EffectiveTo is not null) throw new ConflictException(ClosedMessage);
            CloseOn(current, to, "effectiveTo");
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            var type = await db.SpecialServiceTypes.AsNoTracking().FirstOrDefaultAsync(t => t.SpecialServiceTypeId == current.SpecialServiceTypeId, ct2);
            return (current, type?.Name ?? "", type?.IsActive ?? false);
        }, ct);

        return TripDto(row, typeName, typeActive, Today());
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Chofer del tenant (404) que admite escrituras: un chofer eliminado (terminal) solo se consulta (409).</summary>
    private async Task<Driver> LoadWritableDriverAsync(Guid driverPublicId, CancellationToken ct)
    {
        var driver = await db.ResolveDriverAsync(driverPublicId, false, ct);
        if (await db.IsTerminalAsync(driver.StatusCodeId, ct)) throw new ConflictException(FleetQueries.DriverRetiredMessage);
        return driver;
    }

    private async Task<int> LookupOrFailAsync(string domain, string code, string field, string unknownPrefix, CancellationToken ct)
    {
        var c = code.Trim().ToUpperInvariant();
        return await lookups.TryGetIdAsync(domain, c, ct)
               ?? throw new ValidationException(field, $"{unknownPrefix}: '{code.Trim()}'.");
    }

    private async Task<string> DeliveryDuplicateMessageAsync(int serviceTypeLookupId, int packageTypeLookupId, CancellationToken ct)
    {
        var (_, svc) = await LabelAsync(serviceTypeLookupId, ct);
        var (_, pkg) = await LabelAsync(packageTypeLookupId, ct);
        return $"El chofer ya tiene una tarifa vigente para {svc} + {pkg}; edite esa tarifa o ciérrela antes de agregar otra.";
    }

    private static string TripDuplicateMessage(string typeName)
        => $"El chofer ya tiene una tarifa vigente para el tipo de viaje '{typeName}'; edite esa tarifa o ciérrela antes de agregar otra.";

    private static void RejectKeys(IDictionary<string, System.Text.Json.JsonElement>? extra, string[] keys, string field, string message)
    {
        if (extra is not null && extra.Keys.Any(k => keys.Contains(k, StringComparer.OrdinalIgnoreCase)))
            throw new ValidationException(field, message);
    }

    private static void ValidateRate(decimal rate)
    {
        if (rate < 0) throw new ValidationException("rate", "La tarifa no puede ser negativa.");
        var error = FleetRules.DecimalError(rate, 18, 4);
        if (error is not null) throw new ValidationException("rate", error);
    }

    /// <summary>Principio #8: una versión nueva o un cierre nunca se fechan en el pasado.</summary>
    private static void EnsureNotPast(DateOnly date, string field)
    {
        if (date < Today()) throw new ValidationException(field, RateService.PastDateMessage);
    }

    /// <summary>Cierra la fila en 'date' (exclusivo). Una fecha anterior al inicio de la fila es 400.</summary>
    private static void CloseOn(IEffectiveDated row, DateOnly date, string field)
    {
        if (date < row.EffectiveFrom)
            throw new ValidationException(field, $"La fecha de cierre no puede ser anterior al inicio de la tarifa ({row.EffectiveFrom:yyyy-MM-dd}).");
        try { EffectiveDated.Close(row, date); }
        catch (ArgumentException ex) { throw new ValidationException(field, ex.Message); }
    }

    /// <summary>Valida y cierra la fila abierta para abrir la versión nueva desde newFrom (mismo día: fila de longitud cero).</summary>
    private static void NewVersion(IEffectiveDated open, DateOnly newFrom, string field)
    {
        if (newFrom < open.EffectiveFrom)
            throw new ValidationException(field, $"La nueva vigencia no puede ser anterior al inicio de la tarifa actual ({open.EffectiveFrom:yyyy-MM-dd}).");
        try { EffectiveDated.ValidateNewVersion(open, newFrom); }
        catch (ArgumentException ex) { throw new ValidationException(field, ex.Message); }
        CloseOn(open, newFrom, field);
    }

    private int RequireTenantId() => tenant.TenantId ?? throw new ForbiddenException("No hay tenant activo en la sesión.");

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<(string Code, string Label)> LabelAsync(int lookupCodeId, CancellationToken ct)
    {
        var l = await lookups.GetAsync(lookupCodeId, ct);
        return l is null ? ("", "") : (l.InternalCode, MultilingualText.Resolve(l.LabelJson, tenant.Lang));
    }

    private async Task<DriverDeliveryRateDto> DeliveryDtoAsync(DriverDeliveryRate r, DateOnly asOf, CancellationToken ct)
    {
        var (svcCode, svc) = await LabelAsync(r.ServiceTypeLookupId, ct);
        var (pkgCode, pkg) = await LabelAsync(r.PackageTypeLookupId, ct);
        return new DriverDeliveryRateDto(r.DriverDeliveryRateId, svcCode, svc, pkgCode, pkg, r.Rate, r.EffectiveFrom, r.EffectiveTo,
            EffectiveDated.IsCurrentOn(r, asOf));
    }

    private static DriverAttemptRateDto AttemptDto(DriverAttemptRate r, DateOnly asOf)
        => new(r.AttemptNumber, r.DriverAttemptRateId, r.Rate, r.EffectiveFrom, r.EffectiveTo, EffectiveDated.IsCurrentOn(r, asOf));

    private static DriverTripRateDto TripDto(DriverTripRate r, string typeName, bool typeActive, DateOnly asOf)
        => new(r.DriverTripRateId, r.SpecialServiceTypeId, typeName, typeActive, r.Rate, r.EffectiveFrom, r.EffectiveTo,
            EffectiveDated.IsCurrentOn(r, asOf));
}
