using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 4 (P5) — bitácora de combustible (panel del módulo Flota): lista paginada con km/L y costo/km calculados al leer,
/// alta, corrección y baja lógica (IsActive = 0, nunca DELETE).
/// - El TenantId sale del principal; FuelLog lleva TenantId y se lee bajo el filtro global (además FK compuesta en SQL).
/// - La eficiencia se calcula sobre la serie ACTIVA COMPLETA de cada vehículo (FuelEfficiency.Compute), no sobre la página:
///   una carga inactiva deja de contar y la siguiente se mide contra la anterior activa.
/// - Escrituras con el vehículo bloqueado (FleetQueries.LockVehicleAsync: UPDLOCK, ROWLOCK, TenantId) dentro de
///   RunInTransactionAsync: las cargas concurrentes del mismo vehículo se serializan, así que la validación monótona del
///   odómetro y la subida de Vehicle.CurrentOdometerKm (FleetRules.RaiseOdometer, máximo monotónico) no compiten.
/// - Ningún desbordamiento DECIMAL llega a SQL: FleetRules.DecimalError se aplica antes de guardar (400).
/// </summary>
public sealed class FuelLogService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    public const int MaxTake = 500;
    public const int DefaultTake = 100;
    public const int StationMaxLength = 150;

    public const string LitersMessage = "Los litros deben ser mayores que 0.";
    public const string NegativeCostMessage = "El costo total no puede ser negativo.";
    public const string NegativeOdometerMessage = "El odómetro no puede ser negativo.";
    public const string FutureDateMessage = "La fecha de la carga no puede ser futura.";
    public const string DateRequiredMessage = "La fecha de la carga es obligatoria.";
    public const string VehicleRequiredMessage = "El vehículo de la carga es obligatorio.";
    public const string VehicleImmutableMessage = "El vehículo de una carga no se cambia; desactívela y registre otra.";
    public const string InactiveLogMessage = "La carga de combustible está inactiva; no se puede corregir.";
    public const string StationTooLongMessage = "La estación admite como máximo 150 caracteres.";
    public const string TakeMessage = "take debe estar entre 1 y 500.";
    public const string SkipMessage = "skip no puede ser negativo.";
    public const string SaveConflictMessage = "La carga de combustible cambió mientras se guardaba; recargue e intente de nuevo.";

    /// <summary>Tolerancia de reloj para "fecha futura" (el dispositivo del usuario puede ir unos minutos adelantado).</summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    /// <summary>Campos que el PATCH rechaza aunque lleguen en el cuerpo (van a Extra por no estar en el contrato).</summary>
    private static readonly string[] VehicleFields = { "vehiclePublicId", "vehicleId", "vehicle", "vehicleCode" };

    private sealed record VehicleInfo(Guid PublicId, string Code);
    private sealed record DriverInfo(Guid PublicId, string FullName);

    // ---------------------------------------------------------------- lista

    public async Task<FuelLogPageDto> ListAsync(FuelLogQuery q, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (q.Skip < 0) errors["skip"] = new[] { SkipMessage };
        if (q.Take is < 1 or > MaxTake) errors["take"] = new[] { TakeMessage };
        if (errors.Count > 0) throw new ValidationException(errors);

        var query = db.FuelLogs.AsNoTracking();
        if (!q.IncludeInactive) query = query.Where(f => f.IsActive);
        if (q.VehiclePublicId is Guid vehiclePublicId)
        {
            var vehicle = await db.ResolveVehicleAsync(vehiclePublicId, false, ct);
            var vehicleId = vehicle.VehicleId;
            query = query.Where(f => f.VehicleId == vehicleId);
        }
        if (q.DriverPublicId is Guid driverPublicId)
        {
            var driver = await db.ResolveDriverAsync(driverPublicId, false, ct);
            var driverId = driver.DriverId;
            query = query.Where(f => f.DriverId == driverId);
        }
        // Rango: desde inclusivo, hasta exclusivo.
        if (q.FromUtc is DateTime from) { var f0 = ToUtc(from); query = query.Where(f => f.FillDateUtc >= f0); }
        if (q.ToUtc is DateTime to) { var t0 = ToUtc(to); query = query.Where(f => f.FillDateUtc < t0); }

        // Todas las coincidencias (solo llaves): total, vehículos del resultado y cargas activas para el resumen.
        var matches = await query.Select(f => new { f.FuelLogId, f.VehicleId, f.IsActive }).ToListAsync(ct);
        if (matches.Count == 0)
            return new FuelLogPageDto(0, q.Skip, q.Take, Array.Empty<FuelLogDto>(), Array.Empty<FuelVehicleSummaryDto>());

        var page = await query.OrderByDescending(f => f.FillDateUtc).ThenByDescending(f => f.FuelLogId)
            .Skip(q.Skip).Take(q.Take).ToListAsync(ct);

        var vehicleIds = matches.Select(m => m.VehicleId).Distinct().ToList();
        var efficiency = await EfficiencyByVehicleAsync(vehicleIds, ct);
        var vehicles = await VehicleInfoAsync(vehicleIds, ct);

        var items = await ToDtosAsync(page, efficiency, vehicles, ct);

        // Resumen por vehículo: cargas ACTIVAS que cumplen el filtro (todas las páginas), con la eficiencia de la serie completa.
        var summary = matches.Where(m => m.IsActive)
            .GroupBy(m => m.VehicleId)
            .Select(g =>
            {
                var result = efficiency.GetValueOrDefault(g.Key);
                var rows = g.Select(m => result?.ById.GetValueOrDefault(m.FuelLogId)).Where(r => r is not null).Select(r => r!).ToList();
                var s = FuelEfficiency.Summarize(rows);
                var v = vehicles.GetValueOrDefault(g.Key);
                return new FuelVehicleSummaryDto(v?.PublicId ?? Guid.Empty, v?.Code ?? "", s.Fills, s.TotalLiters, s.TotalCost,
                    s.DistanceKm, s.KmPerLiter, s.CostPerKm);
            })
            .OrderBy(s => s.VehicleCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FuelLogPageDto(matches.Count, q.Skip, q.Take, items, summary);
    }

    // ---------------------------------------------------------------- alta

    public async Task<FuelLogDto> CreateAsync(FuelLogCreateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var errors = new Dictionary<string, string[]>();
        if (req.VehiclePublicId == Guid.Empty) errors["vehiclePublicId"] = new[] { VehicleRequiredMessage };
        var fillDate = ValidateFillDate(req.FillDateUtc, errors);
        ValidateAmounts(req.Liters, req.TotalCost, req.OdometerKm, errors);
        var station = Station(req.Station, errors);
        var currencyId = await CurrencyAsync(req.Currency, errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        // Identidad fuera de la transacción (404 rápidos); la fila del vehículo se bloquea dentro.
        var vehicleId = (await db.ResolveVehicleAsync(req.VehiclePublicId, false, ct)).VehicleId;
        int? driverId = req.DriverPublicId is Guid dp ? (await db.ResolveDriverAsync(dp, false, ct)).DriverId : null;

        var id = await db.RunInTransactionAsync(async ct2 =>
        {
            var vehicle = await LockWritableVehicleAsync(vehicleId, ct2);

            var series = await ActiveSeriesAsync(vehicleId, ct2);
            if (FuelEfficiency.ValidateOdometer(series, fillDate, req.OdometerKm, null) is string odoError)
                throw new ValidationException("odometerKm", odoError);

            var log = new FuelLog
            {
                TenantId = tenantId, VehicleId = vehicleId, DriverId = driverId, FillDateUtc = fillDate,
                OdometerKm = req.OdometerKm, Liters = req.Liters, TotalCost = req.TotalCost,
                CurrencyLookupId = currencyId, Station = station, IsActive = true,
            };
            db.FuelLogs.Add(log);
            RaiseVehicleOdometer(vehicle, req.OdometerKm);
            await db.SaveGuardedAsync(SaveConflictMessage, ct2);
            return log.FuelLogId;
        }, ct);

        return await GetDtoAsync(id, ct);
    }

    // ---------------------------------------------------------------- corrección

    /// <summary>
    /// PATCH: null = sin cambio; clearOdometer/clearDriver quitan el valor; currency/station "" = quitar. Mismas validaciones y
    /// bloqueo que el alta, excluyendo la propia fila. El vehículo no se cambia (400).
    /// </summary>
    public async Task<FuelLogDto> UpdateAsync(int id, FuelLogPatchRequest req, CancellationToken ct)
    {
        if (req.Extra is not null && req.Extra.Keys.Any(k => VehicleFields.Contains(k, StringComparer.OrdinalIgnoreCase)))
            throw new ValidationException("vehiclePublicId", VehicleImmutableMessage);

        var errors = new Dictionary<string, string[]>();
        DateTime? fillDate = req.FillDateUtc is DateTime fd ? ValidateFillDate(fd, errors) : null;
        if (req.Liters is decimal liters) ValidateLiters(liters, errors);
        if (req.TotalCost is decimal cost) ValidateCost(cost, errors);
        if (req.ClearOdometer != true && req.OdometerKm is decimal odo) ValidateOdometerValue(odo, errors);
        var station = req.Station is null ? null : Station(req.Station, errors);
        var currencyId = req.Currency is null ? null : await CurrencyAsync(req.Currency, errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        // Carga de otro tenant (o inexistente) → 404 por el filtro global.
        var current = await db.FuelLogs.AsNoTracking().Where(f => f.FuelLogId == id)
            .Select(f => new { f.FuelLogId, f.VehicleId }).FirstOrDefaultAsync(ct)
            ?? throw NotFound();

        int? driverId = null;
        if (req.ClearDriver != true && req.DriverPublicId is Guid dp) driverId = (await db.ResolveDriverAsync(dp, false, ct)).DriverId;

        await db.RunInTransactionAsync(async ct2 =>
        {
            var vehicle = await LockWritableVehicleAsync(current.VehicleId, ct2);
            var log = await db.FuelLogs.FirstOrDefaultAsync(f => f.FuelLogId == id, ct2) ?? throw NotFound();
            if (!log.IsActive) throw new ConflictException(InactiveLogMessage);

            if (fillDate is DateTime newDate) log.FillDateUtc = newDate;
            if (req.Liters is decimal l) log.Liters = l;
            if (req.TotalCost is decimal c) log.TotalCost = c;
            if (req.ClearOdometer == true) log.OdometerKm = null;
            else if (req.OdometerKm is decimal o) log.OdometerKm = o;
            if (req.ClearDriver == true) log.DriverId = null;
            else if (driverId.HasValue) log.DriverId = driverId;
            if (req.Currency is not null) log.CurrencyLookupId = currencyId;
            if (req.Station is not null) log.Station = station;

            var series = await ActiveSeriesAsync(log.VehicleId, ct2);
            if (FuelEfficiency.ValidateOdometer(series, log.FillDateUtc, log.OdometerKm, log.FuelLogId) is string odoError)
                throw new ValidationException("odometerKm", odoError);

            RaiseVehicleOdometer(vehicle, log.OdometerKm);
            await db.SaveGuardedAsync(SaveConflictMessage, ct2);
        }, ct);

        return await GetDtoAsync(id, ct);
    }

    // ---------------------------------------------------------------- baja lógica

    /// <summary>IsActive = 0: la carga deja de contar en la eficiencia. El odómetro del vehículo NO baja. Idempotente.</summary>
    public async Task<FuelLogDto> DeactivateAsync(int id, CancellationToken ct)
    {
        var log = await db.FuelLogs.FirstOrDefaultAsync(f => f.FuelLogId == id, ct) ?? throw NotFound();
        if (log.IsActive)
        {
            log.IsActive = false;
            await db.SaveGuardedAsync(SaveConflictMessage, ct);
        }
        return await GetDtoAsync(id, ct);
    }

    // ---------------------------------------------------------------- helpers de lectura

    private async Task<FuelLogDto> GetDtoAsync(int id, CancellationToken ct)
    {
        var log = await db.FuelLogs.AsNoTracking().FirstOrDefaultAsync(f => f.FuelLogId == id, ct) ?? throw NotFound();
        var vehicleIds = new List<int> { log.VehicleId };
        var efficiency = await EfficiencyByVehicleAsync(vehicleIds, ct);
        var vehicles = await VehicleInfoAsync(vehicleIds, ct);
        return (await ToDtosAsync(new List<FuelLog> { log }, efficiency, vehicles, ct))[0];
    }

    private async Task<IReadOnlyList<FuelLogDto>> ToDtosAsync(List<FuelLog> logs, IReadOnlyDictionary<int, FuelEfficiencyResult> efficiency,
        IReadOnlyDictionary<int, VehicleInfo> vehicles, CancellationToken ct)
    {
        var driverIds = logs.Where(l => l.DriverId.HasValue).Select(l => l.DriverId!.Value).Distinct().ToList();
        var drivers = driverIds.Count == 0
            ? new Dictionary<int, DriverInfo>()
            : await db.Drivers.AsNoTracking().Where(d => driverIds.Contains(d.DriverId))
                .Select(d => new { d.DriverId, d.PublicId, d.FullName })
                .ToDictionaryAsync(d => d.DriverId, d => new DriverInfo(d.PublicId, d.FullName), ct);

        var items = new List<FuelLogDto>(logs.Count);
        foreach (var l in logs)
        {
            var v = vehicles.GetValueOrDefault(l.VehicleId);
            var d = l.DriverId is int did ? drivers.GetValueOrDefault(did) : null;
            // Una carga inactiva no está en la serie: sin eficiencia.
            var e = l.IsActive ? efficiency.GetValueOrDefault(l.VehicleId)?.ById.GetValueOrDefault(l.FuelLogId) : null;
            var currency = l.CurrencyLookupId is int cid ? (await lookups.GetAsync(cid, ct))?.InternalCode : null;
            items.Add(new FuelLogDto(l.FuelLogId, v?.PublicId ?? Guid.Empty, v?.Code ?? "", d?.PublicId, d?.FullName,
                l.FillDateUtc, l.OdometerKm, l.Liters, l.TotalCost, currency, l.Station,
                e?.DistanceKm, e?.KmPerLiter, e?.CostPerKm, l.IsActive));
        }
        return items;
    }

    /// <summary>Eficiencia de la serie activa COMPLETA de cada vehículo (una consulta para todos, sin N+1).</summary>
    private async Task<Dictionary<int, FuelEfficiencyResult>> EfficiencyByVehicleAsync(List<int> vehicleIds, CancellationToken ct)
    {
        var readings = await db.FuelLogs.AsNoTracking()
            .Where(f => f.IsActive && vehicleIds.Contains(f.VehicleId))
            .Select(f => new { f.VehicleId, f.FuelLogId, f.FillDateUtc, f.OdometerKm, f.Liters, f.TotalCost })
            .ToListAsync(ct);
        return readings.GroupBy(r => r.VehicleId).ToDictionary(g => g.Key,
            g => FuelEfficiency.Compute(g.Select(r => new FuelReading(r.FuelLogId, r.FillDateUtc, r.OdometerKm, r.Liters, r.TotalCost))));
    }

    private async Task<Dictionary<int, VehicleInfo>> VehicleInfoAsync(List<int> vehicleIds, CancellationToken ct)
        => await db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.VehicleId))
            .Select(v => new { v.VehicleId, v.PublicId, v.Code })
            .ToDictionaryAsync(v => v.VehicleId, v => new VehicleInfo(v.PublicId, v.Code), ct);

    /// <summary>Cargas activas de un vehículo (para la validación monótona). Se lee con el vehículo ya bloqueado.</summary>
    private async Task<List<FuelReading>> ActiveSeriesAsync(int vehicleId, CancellationToken ct)
        => (await db.FuelLogs.AsNoTracking()
                .Where(f => f.IsActive && f.VehicleId == vehicleId)
                .Select(f => new { f.FuelLogId, f.FillDateUtc, f.OdometerKm, f.Liters, f.TotalCost })
                .ToListAsync(ct))
            .Select(f => new FuelReading(f.FuelLogId, f.FillDateUtc, f.OdometerKm, f.Liters, f.TotalCost))
            .ToList();

    // ---------------------------------------------------------------- helpers de escritura

    /// <summary>Carga el vehículo con bloqueo de fila; debe estar activo y no terminal (409).</summary>
    private async Task<Vehicle> LockWritableVehicleAsync(int vehicleId, CancellationToken ct)
    {
        var vehicle = await db.LockVehicleAsync(vehicleId, ct) ?? throw new NotFoundException("Vehículo");
        if (!vehicle.IsActive || await db.IsTerminalAsync(vehicle.StatusCodeId, ct))
            throw new ConflictException(FleetQueries.VehicleInactiveMessage);
        return vehicle;
    }

    /// <summary>El odómetro del vehículo solo sube (máximo monotónico); sin lectura no cambia.</summary>
    private static void RaiseVehicleOdometer(Vehicle vehicle, decimal? reading)
    {
        var raised = FleetRules.RaiseOdometer(vehicle.CurrentOdometerKm, reading);
        if (raised != vehicle.CurrentOdometerKm) vehicle.CurrentOdometerKm = raised;
    }

    // ---------------------------------------------------------------- validación

    private static DateTime ValidateFillDate(DateTime value, IDictionary<string, string[]> errors)
    {
        if (value == default) { errors["fillDateUtc"] = new[] { DateRequiredMessage }; return value; }
        var utc = ToUtc(value);
        if (utc > DateTime.UtcNow + FutureTolerance) errors["fillDateUtc"] = new[] { FutureDateMessage };
        return utc;
    }

    private static void ValidateAmounts(decimal liters, decimal totalCost, decimal? odometerKm, IDictionary<string, string[]> errors)
    {
        ValidateLiters(liters, errors);
        ValidateCost(totalCost, errors);
        if (odometerKm is decimal odo) ValidateOdometerValue(odo, errors);
    }

    private static void ValidateLiters(decimal liters, IDictionary<string, string[]> errors)
    {
        if (liters <= 0) errors["liters"] = new[] { LitersMessage };
        else if (FleetRules.DecimalError(liters, 10, 3) is string e) errors["liters"] = new[] { e };
    }

    private static void ValidateCost(decimal cost, IDictionary<string, string[]> errors)
    {
        if (cost < 0) errors["totalCost"] = new[] { NegativeCostMessage };
        else if (FleetRules.DecimalError(cost, 18, 4) is string e) errors["totalCost"] = new[] { e };
    }

    private static void ValidateOdometerValue(decimal km, IDictionary<string, string[]> errors)
    {
        if (km < 0) errors["odometerKm"] = new[] { NegativeOdometerMessage };
        else if (FleetRules.DecimalError(km, 12, 1) is string e) errors["odometerKm"] = new[] { e };
    }

    /// <summary>null o "" → sin estación; más de 150 caracteres → error.</summary>
    private static string? Station(string? value, IDictionary<string, string[]> errors)
    {
        if (value is null) return null;
        var v = value.Trim();
        if (v.Length == 0) return null;
        if (v.Length > StationMaxLength) errors["station"] = new[] { StationTooLongMessage };
        return v;
    }

    /// <summary>Moneda opcional por código (catálogo Currency). null o "" → sin moneda; desconocida → error del campo.</summary>
    private async Task<int?> CurrencyAsync(string? code, IDictionary<string, string[]> errors, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var id = await lookups.TryGetIdAsync(LookupDomains.Currency, code.Trim().ToUpperInvariant(), ct);
        if (id is null) errors["currency"] = new[] { $"Moneda desconocida: '{code.Trim()}'." };
        return id;
    }

    /// <summary>Sin zona: 'Z' o sin indicar se toman como UTC; con zona local se convierte.</summary>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static NotFoundException NotFound() => new("Carga de combustible", feminine: true);
}
