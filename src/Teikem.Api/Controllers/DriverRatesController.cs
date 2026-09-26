using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 4 (P6): tarifas del chofer (detalle 'Tarifas' de 'Choferes y tarifas', grupo Catálogo; módulo 11A).
/// Lectura con driverpay.view y escrituras con driverpay.manage: flota y compensación van separadas (R8). El chofer sale
/// siempre de la ruta (R31); los ids de tarifa solo valen bajo SU chofer (404 si no). Efectivo-fechadas: editar = cerrar y
/// abrir, quitar = cerrar. Un chofer eliminado solo se consulta (409 en escrituras).
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class DriverRatesController(DriverRateService rates) : ControllerBase
{
    /// <summary>Tarifas del chofer vigentes en asOf (hoy por defecto); includeHistory agrega las cerradas. Intentos: un renglón por nivel.</summary>
    [HttpGet("drivers/{publicId:guid}/rates"), RequirePermission(PermissionCatalog.DriverPayView)]
    public Task<DriverRatesDto> Get(Guid publicId, [FromQuery] DateOnly? asOf, [FromQuery] bool includeHistory, CancellationToken ct)
        => rates.GetAsync(publicId, asOf, includeHistory, ct);

    // ---------------- Por entrega ----------------

    /// <summary>Alta de tarifa por entrega (servicio + tipo de paquete). 409 si ya hay una vigente para el par.</summary>
    [HttpPost("drivers/{publicId:guid}/delivery-rates"), RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverDeliveryRateDto> AddDelivery(Guid publicId, [FromBody] DriverDeliveryRateCreateRequest req, CancellationToken ct)
        => rates.AddDeliveryRateAsync(publicId, req, ct);

    /// <summary>Nueva versión de la tarifa por entrega (devuelve la fila nueva). Servicio y paquete son inmutables.</summary>
    [HttpPatch("drivers/{publicId:guid}/delivery-rates/{id:int}"), RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverDeliveryRateDto> UpdateDelivery(Guid publicId, int id, [FromBody] DriverRateUpdateRequest req, CancellationToken ct)
        => rates.UpdateDeliveryRateAsync(publicId, id, req, ct);

    /// <summary>Quitar conservando historial: cierra la fila en effectiveTo (hoy por defecto).</summary>
    [HttpPost("drivers/{publicId:guid}/delivery-rates/{id:int}/close"), RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverDeliveryRateDto> CloseDelivery(Guid publicId, int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DriverRateCloseRequest? req, CancellationToken ct)
        => rates.CloseDeliveryRateAsync(publicId, id, req, ct);

    // ---------------- Por intento ----------------

    /// <summary>Fija la tarifa del intento n (1..niveles de la compañía): crea la fila o la versiona.</summary>
    [HttpPut("drivers/{publicId:guid}/attempt-rates/{attemptNumber:int}"), RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverAttemptRateDto> SetAttempt(Guid publicId, int attemptNumber, [FromBody] DriverRateUpdateRequest req, CancellationToken ct)
        => rates.SetAttemptRateAsync(publicId, attemptNumber, req, ct);

    /// <summary>Cierra la tarifa vigente del intento n (el nivel queda 'sin tarifa').</summary>
    [HttpPost("drivers/{publicId:guid}/attempt-rates/{attemptNumber:int}/close"), RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverAttemptRateDto> CloseAttempt(Guid publicId, int attemptNumber,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DriverRateCloseRequest? req, CancellationToken ct)
        => rates.CloseAttemptRateAsync(publicId, attemptNumber, req, ct);

    // ---------------- Por viaje ----------------

    /// <summary>Alta de tarifa por viaje de un tipo del catálogo de servicios especiales. 409 si ya hay una vigente de ese tipo.</summary>
    [HttpPost("drivers/{publicId:guid}/trip-rates"), RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverTripRateDto> AddTrip(Guid publicId, [FromBody] DriverTripRateCreateRequest req, CancellationToken ct)
        => rates.AddTripRateAsync(publicId, req, ct);

    /// <summary>Nueva versión de la tarifa por viaje (devuelve la fila nueva). El tipo es inmutable.</summary>
    [HttpPatch("drivers/{publicId:guid}/trip-rates/{id:int}"), RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverTripRateDto> UpdateTrip(Guid publicId, int id, [FromBody] DriverRateUpdateRequest req, CancellationToken ct)
        => rates.UpdateTripRateAsync(publicId, id, req, ct);

    /// <summary>Quitar conservando historial: cierra la tarifa por viaje en effectiveTo (hoy por defecto).</summary>
    [HttpPost("drivers/{publicId:guid}/trip-rates/{id:int}/close"), RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverTripRateDto> CloseTrip(Guid publicId, int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DriverRateCloseRequest? req, CancellationToken ct)
        => rates.CloseTripRateAsync(publicId, id, req, ct);

    /// <summary>Selector 'tipo de viaje': tipos activos del catálogo de servicios especiales de la compañía (R21).</summary>
    [HttpGet("driver-trip-types"), RequirePermission(PermissionCatalog.DriverPayView)]
    public Task<IReadOnlyList<DriverTripTypeDto>> TripTypes(CancellationToken ct)
        => rates.GetTripTypesAsync(ct);
}
