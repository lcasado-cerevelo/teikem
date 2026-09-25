using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 2 (P5): servicios especiales por cliente (componente 5 del modelo de facturación, tarifa efectivo-fechada) y
/// tipos compartidos del tenant. Requiere el módulo CATALOG. Alta solo con contrato vigente y el componente encendido (409);
/// las escrituras exigen la capacidad EDIT_CONTRACT en el estatus del contrato vigente (422 en EXPIRED/CANCELLED por defecto).
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class SpecialServicesController(SpecialServiceService specials) : ControllerBase
{
    /// <summary>Servicios especiales del cliente vigentes en asOf (hoy por defecto); includeHistory devuelve también las filas cerradas.</summary>
    [HttpGet("clients/{publicId:guid}/special-services"), RequirePermission(PermissionCatalog.ContractsRead)]
    public Task<SpecialServiceListDto> List(Guid publicId, [FromQuery] DateOnly? asOf, [FromQuery] bool includeHistory, CancellationToken ct)
        => specials.GetForClientAsync(publicId, asOf, includeHistory, ct);

    /// <summary>Alta: typeId (tipo existente) o newTypeName (se reutiliza si ya existe uno con el mismo nombre normalizado; si no, se crea para todo el tenant).</summary>
    [HttpPost("clients/{publicId:guid}/special-services"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<SpecialServiceDto> Add(Guid publicId, [FromBody] SpecialServiceCreateRequest req, CancellationToken ct)
        => specials.AddAsync(publicId, req, ct);

    /// <summary>Nueva versión de la tarifa: cierra la fila abierta y abre otra (devuelve la fila nueva). El tipo es inmutable.</summary>
    [HttpPatch("clients/{publicId:guid}/special-services/{id:int}"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<SpecialServiceDto> UpdateRate(Guid publicId, int id, [FromBody] SpecialServiceRateUpdateRequest req, CancellationToken ct)
        => specials.UpdateRateAsync(publicId, id, req, ct);

    /// <summary>Quitar conservando historial: cierra la fila en effectiveTo (hoy por defecto).</summary>
    [HttpPost("clients/{publicId:guid}/special-services/{id:int}/close"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<SpecialServiceDto> Close(Guid publicId, int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SpecialServiceCloseRequest? req, CancellationToken ct)
        => specials.CloseAsync(publicId, id, req ?? new SpecialServiceCloseRequest(), ct);

    /// <summary>Tipos de servicio especial del tenant (alimentará el selector 'tipo de viaje' de Choferes y tarifas).</summary>
    [HttpGet("special-service-types"), RequirePermission(PermissionCatalog.ContractsRead)]
    public Task<IReadOnlyList<SpecialServiceTypeDto>> Types([FromQuery] bool includeInactive, CancellationToken ct)
        => specials.GetTypesAsync(includeInactive, ct);

    /// <summary>Baja lógica del tipo (nunca DELETE): sale del selector de todos los clientes; 409 si algún cliente tiene una tarifa abierta de ese tipo.</summary>
    [HttpPost("special-service-types/{id:int}/deactivate"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<SpecialServiceTypeDto> DeactivateType(int id, CancellationToken ct)
        => specials.DeactivateTypeAsync(id, ct);

    /// <summary>Reactivación del tipo: vuelve al selector con su nombre e historial.</summary>
    [HttpPost("special-service-types/{id:int}/reactivate"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<SpecialServiceTypeDto> ReactivateType(int id, CancellationToken ct)
        => specials.ReactivateTypeAsync(id, ct);
}
