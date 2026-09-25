using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 2 (P4): tarifas del contrato — "por servicio" (monto fijo por servicio y tipo de paquete) y "pieza extra"
/// (tramos marginales por número de pieza). Historial efectivo-fechado: editar cierra la fila vigente y abre otra;
/// quitar = cerrar. Servicio y paquete son inmutables (R25). Requiere el módulo CATALOG y la capacidad EDIT_CONTRACT
/// en el estatus actual del contrato para toda escritura (422 en EXPIRED/CANCELLED por defecto).
/// </summary>
[ApiController]
[Route("api/v1/contracts/{publicId:guid}/rate-components")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class ContractRatesController(RateService rates) : ControllerBase
{
    /// <summary>Tarifas vigentes en asOf (vacío = hoy); includeHistory devuelve también las cerradas.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.ContractsRead)]
    public Task<RateComponentsDto> Get(Guid publicId, [FromQuery] DateOnly? asOf, [FromQuery] bool includeHistory, CancellationToken ct)
        => rates.GetForContractAsync(publicId, asOf, includeHistory, ct);

    /// <summary>Alta: kind PER_SERVICE (con rate) o EXTRA_PIECE (los tramos se agregan después). 409 si el componente está apagado o ya hay fila vigente.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<RateComponentDto> Add(Guid publicId, [FromBody] RateComponentCreateRequest req, CancellationToken ct)
        => rates.AddComponentAsync(publicId, req, ct);

    /// <summary>Nueva versión de una tarifa por servicio: cierra la vigente y abre otra con el monto nuevo (devuelve la fila nueva).</summary>
    [HttpPatch("{id:int}"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<RateComponentDto> UpdateRate(Guid publicId, int id, [FromBody] RateUpdateRequest req, CancellationToken ct)
        => rates.UpdateRateAsync(publicId, id, req, ct);

    /// <summary>Quitar conservando historial: cierra el componente (y sus tramos abiertos) en effectiveTo (vacío = hoy).</summary>
    [HttpPost("{id:int}/close"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<RateComponentDto> Close(Guid publicId, int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RateCloseRequest? req, CancellationToken ct)
        => rates.CloseComponentAsync(publicId, id, req, ct);

    /// <summary>Tramo nuevo de pieza extra: fromUnit >= 2, toUnit vacío = abierto, sin traslape con los vigentes.</summary>
    [HttpPost("{id:int}/tiers"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<TierDto> AddTier(Guid publicId, int id, [FromBody] TierUpsertRequest req, CancellationToken ct)
        => rates.AddTierAsync(publicId, id, req, ct);

    /// <summary>Nueva versión de un tramo: cierra el vigente y abre otro (devuelve el tramo nuevo).</summary>
    [HttpPatch("{id:int}/tiers/{tierId:int}"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<TierDto> UpdateTier(Guid publicId, int id, int tierId, [FromBody] TierPatchRequest req, CancellationToken ct)
        => rates.UpdateTierAsync(publicId, id, tierId, req, ct);

    /// <summary>Cierra un tramo conservando el historial.</summary>
    [HttpPost("{id:int}/tiers/{tierId:int}/close"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<TierDto> CloseTier(Guid publicId, int id, int tierId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RateCloseRequest? req, CancellationToken ct)
        => rates.CloseTierAsync(publicId, id, tierId, req, ct);
}
