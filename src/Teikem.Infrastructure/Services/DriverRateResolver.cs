using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 4 (P6) — Resolvedor de tarifas del chofer (costura para la entrega especial y la liquidación del Lote 9).
/// - Consulta las filas activas del chofer bajo el filtro global de tenant (las tablas de tarifa son ITenantScoped) y
///   elige la vigente en asOf con DriverPayoutRules.PickCurrent (EffectiveTo exclusivo).
/// - Sin fila vigente: Missing = true, Amount = 0, RateId = null (R15: el pago se crea igual y el vacío queda visible).
/// - No valida que el chofer exista ni su estatus: eso es de quien llama (ResolveDriverAsync / disponibilidad).
/// </summary>
public sealed class DriverRateResolver(TeikemDbContext db, ILookupCache lookups) : IDriverRateResolver
{
    public async Task<DriverRateResolution> DeliveryRateAsync(int driverId, int serviceTypeLookupId, int packageTypeLookupId, DateOnly asOf, CancellationToken ct)
    {
        var rows = await db.DriverDeliveryRates.AsNoTracking()
            .Where(r => r.DriverId == driverId && r.ServiceTypeLookupId == serviceTypeLookupId && r.PackageTypeLookupId == packageTypeLookupId
                        && r.IsActive && r.EffectiveFrom <= asOf && (r.EffectiveTo == null || r.EffectiveTo > asOf))
            .ToListAsync(ct);
        var pick = DriverPayoutRules.PickCurrent(rows, asOf);
        return pick is null ? Missing() : new DriverRateResolution(pick.Rate, pick.DriverDeliveryRateId, false);
    }

    public async Task<IReadOnlyDictionary<int, decimal>> AttemptRatesAsync(int driverId, DateOnly asOf, CancellationToken ct)
    {
        var rows = await db.DriverAttemptRates.AsNoTracking()
            .Where(r => r.DriverId == driverId && r.IsActive && r.EffectiveFrom <= asOf && (r.EffectiveTo == null || r.EffectiveTo > asOf))
            .ToListAsync(ct);
        var result = new Dictionary<int, decimal>();
        foreach (var level in rows.GroupBy(r => r.AttemptNumber))
        {
            var pick = DriverPayoutRules.PickCurrent(level, asOf);
            if (pick is not null) result[level.Key] = pick.Rate;
        }
        return result;
    }

    public async Task<DriverRateResolution> TripRateAsync(int driverId, int specialServiceTypeId, DateOnly asOf, CancellationToken ct)
    {
        var rows = await db.DriverTripRates.AsNoTracking()
            .Where(r => r.DriverId == driverId && r.SpecialServiceTypeId == specialServiceTypeId
                        && r.IsActive && r.EffectiveFrom <= asOf && (r.EffectiveTo == null || r.EffectiveTo > asOf))
            .ToListAsync(ct);
        var pick = DriverPayoutRules.PickCurrent(rows, asOf);
        return pick is null ? Missing() : new DriverRateResolution(pick.Rate, pick.DriverTripRateId, false);
    }

    public Task<DriverPayPolicySnapshot> PolicyAsync(CancellationToken ct)
        => DriverPayPolicyService.SnapshotAsync(db, lookups, ct);

    private static DriverRateResolution Missing() => new(0m, null, true);
}
