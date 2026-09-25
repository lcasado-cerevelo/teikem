using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 2 (P3): contratos del cliente: modelo de facturación (5 checkboxes), cargo por despacho, cargo por COD,
/// niveles de servicio y estatus (DRAFT → ACTIVE → EXPIRED/CANCELLED vía StatusService). Requiere el módulo CATALOG.
/// Toda escritura de negocio exige la capacidad EDIT_CONTRACT en el estatus actual (422 en EXPIRED/CANCELLED por defecto).
/// </summary>
[ApiController]
[Route("api/v1/contracts")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class ContractsController(ContractService contracts) : ControllerBase
{
    /// <summary>Lista. clientId = solo los del cliente; includeInactive incluye los dados de baja.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.ContractsRead)]
    public Task<IReadOnlyList<ContractSummaryDto>> List([FromQuery] Guid? clientId, [FromQuery] bool includeInactive, CancellationToken ct)
        => contracts.GetListAsync(clientId, includeInactive, ct);

    /// <summary>Contrato adicional (el inicial nace con el cliente). Arranca en DRAFT con "Por servicio" encendido.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.ContractsCreate)]
    public Task<ContractDetailDto> Create([FromBody] ContractCreateRequest req, CancellationToken ct) => contracts.CreateAsync(req, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.ContractsRead)]
    public Task<ContractDetailDto> Get(Guid publicId, CancellationToken ct) => contracts.GetAsync(publicId, ct);

    /// <summary>Datos generales (título, fecha fin, auto-renovación, moneda, disparador de cobro, notas).</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<ContractDetailDto> Update(Guid publicId, [FromBody] ContractPatchRequest req, CancellationToken ct) => contracts.UpdateAsync(publicId, req, ct);

    /// <summary>Los 5 checkboxes del modelo de facturación. Desmarcar no borra lo configurado (R7b).</summary>
    [HttpPatch("{publicId:guid}/billing-model"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<ContractDetailDto> SetBillingModel(Guid publicId, [FromBody] BillingModelRequest req, CancellationToken ct) => contracts.SetBillingModelAsync(publicId, req, ct);

    /// <summary>Cargo por despacho: monto por orden.</summary>
    [HttpPatch("{publicId:guid}/dispatch-fee"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<ContractDetailDto> SetDispatchFee(Guid publicId, [FromBody] DispatchFeeRequest req, CancellationToken ct) => contracts.SetDispatchFeeAsync(publicId, req, ct);

    /// <summary>Cargo por COD: FIXED (monto por orden) o PERCENT (por ciento del monto COD cobrado).</summary>
    [HttpPatch("{publicId:guid}/cod-fee"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<ContractDetailDto> SetCodFee(Guid publicId, [FromBody] CodFeeRequest req, CancellationToken ct) => contracts.SetCodFeeAsync(publicId, req, ct);

    /// <summary>Reemplazo completo de los niveles de servicio (los ausentes se dan de baja; uno por tipo de servicio).</summary>
    [HttpPut("{publicId:guid}/service-levels"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<ContractDetailDto> SetServiceLevels(Guid publicId, [FromBody] IList<ServiceLevelUpsert> items, CancellationToken ct) => contracts.SetServiceLevelsAsync(publicId, items, ct);

    /// <summary>Cambio de estatus vía StatusService (DRAFT → ACTIVE → EXPIRED/CANCELLED). Activar exige cliente no suspendido y ningún otro contrato ACTIVE.</summary>
    [HttpPost("{publicId:guid}/status"), RequirePermission(PermissionCatalog.ContractsUpdate)]
    public Task<ContractDetailDto> TransitionStatus(Guid publicId, [FromBody] StatusChangeRequest req, CancellationToken ct) => contracts.TransitionStatusAsync(publicId, req, ct);
}
