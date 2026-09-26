using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Contracts;

namespace Teikem.Infrastructure.Fleet;

/// <summary>
/// Disponibilidad para despacho (R7): única implementación de las reglas (FleetAvailabilityRules) sobre datos del tenant.
/// La usan el panel de flota, la entrega especial con chofer y el futuro planificador de Despacho.
/// </summary>
public interface IFleetAvailabilityService
{
    /// <summary>Choferes y vehículos no dados de baja definitiva con su disponibilidad en la fecha; onlyAvailable filtra.</summary>
    Task<FleetAvailabilityDto> GetAsync(DateOnly date, bool onlyAvailable, CancellationToken ct);

    /// <summary>Evalúa un chofer del tenant (404 'Chofer no encontrado.').</summary>
    Task<AvailabilityResult> CheckDriverAsync(int driverId, DateOnly date, CancellationToken ct);

    /// <summary>Evalúa un vehículo del tenant (404 'Vehículo no encontrado.').</summary>
    Task<AvailabilityResult> CheckVehicleAsync(int vehicleId, DateOnly date, CancellationToken ct);
}
