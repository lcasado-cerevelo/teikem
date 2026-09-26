using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 4 (P7): viajes pagados al chofer (DriverTrip) bajo 'Choferes y tarifas' (módulo CATALOG). Compensación separada de
/// la flota (R8): lectura con driverpay.view y escritura con driverpay.manage. El chofer sale SIEMPRE de la ruta (R31);
/// un viaje de otro chofer u otro tenant responde 404. Historial: /api/v1/status/history/DRIVER_TRIP/{id}.
/// </summary>
[ApiController]
[Route("api/v1/drivers/{publicId:guid}/trips")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class DriverTripsController(DriverTripService trips) : ControllerBase
{
    /// <summary>Viajes del chofer; por defecto solo vigentes (includeCancelled agrega los cancelados). from/to inclusivos sobre la fecha del viaje.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.DriverPayView)]
    public Task<IReadOnlyList<DriverTripDto>> List(Guid publicId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string[]? status, [FromQuery] bool includeCancelled, CancellationToken ct)
        => trips.ListAsync(publicId, new DriverTripListQuery(from, to, status is { Length: > 0 } ? status : null, includeCancelled), ct);

    /// <summary>Alta manual: monto congelado con la tarifa por viaje vigente en la fecha (sin tarifa: $0 y rateMissing).</summary>
    [HttpPost, RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverTripDto> Create(Guid publicId, [FromBody] DriverTripCreateRequest req, CancellationToken ct)
        => trips.CreateAsync(publicId, req, ct);

    /// <summary>Cancelar (OPEN → CANCELLED; queda fuera de los vigentes). El viaje de una entrega especial se cancela con la orden (409).</summary>
    [HttpPost("{tripPublicId:guid}/cancel"), RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverTripDto> Cancel(Guid publicId, Guid tripPublicId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DriverTripCancelRequest? req, CancellationToken ct)
        => trips.CancelAsync(publicId, tripPublicId, req?.Comment, ct);
}
