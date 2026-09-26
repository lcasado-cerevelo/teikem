namespace Teikem.Infrastructure.Fleet;

/// <summary>Tarifa resuelta: Missing = sin fila vigente (Amount 0, RateId null; R15: el pago se crea igual y el vacío queda visible).</summary>
public sealed record DriverRateResolution(decimal Amount, int? RateId, bool Missing);

/// <summary>Política de pago vigente del tenant (sin fila: 2 niveles y DELIVERY_PLUS_ATTEMPTS).</summary>
public sealed record DriverPayPolicySnapshot(int AttemptLevels, string FormulaCode);

/// <summary>
/// Costura para la entrega especial (Lote 4) y la liquidación de choferes (Lote 9): resuelve la tarifa vigente del chofer en
/// una fecha (EffectiveTo exclusivo) bajo el filtro de tenant. No valida la existencia ni el estatus del chofer.
/// </summary>
public interface IDriverRateResolver
{
    Task<DriverRateResolution> DeliveryRateAsync(int driverId, int serviceTypeLookupId, int packageTypeLookupId, DateOnly asOf, CancellationToken ct);

    /// <summary>Tarifa vigente por nivel de intento (solo los niveles con fila vigente).</summary>
    Task<IReadOnlyDictionary<int, decimal>> AttemptRatesAsync(int driverId, DateOnly asOf, CancellationToken ct);

    Task<DriverRateResolution> TripRateAsync(int driverId, int specialServiceTypeId, DateOnly asOf, CancellationToken ct);

    Task<DriverPayPolicySnapshot> PolicyAsync(CancellationToken ct);
}
