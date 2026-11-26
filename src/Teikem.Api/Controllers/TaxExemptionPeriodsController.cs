using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 248 — exención de producto por temporada (módulo TAX, permiso tax.manage). No hay DELETE: un período se da de baja (deactivate) y se reactiva.
/// Programado se edita todo; ya empezado solo se mueve la fecha final.
/// </summary>
[ApiController]
[Route("api/v1/tax/exemption-periods")]
[Authorize]
[RequireModule(ModuleKeys.Tax)]
public sealed class TaxExemptionPeriodsController(TaxExemptionPeriodService periods) : ControllerBase
{
    /// <summary>Los períodos (el más reciente primero); includeInactive = también los dados de baja.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.TaxManage)]
    public Task<IReadOnlyList<TaxExemptionPeriodDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => periods.ListAsync(includeInactive, ct);

    [HttpPost, RequirePermission(PermissionCatalog.TaxManage)]
    public Task<TaxExemptionPeriodDto> Create([FromBody] TaxExemptionPeriodRequest req, CancellationToken ct) => periods.CreateAsync(req, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<TaxExemptionPeriodDto> Get(Guid publicId, CancellationToken ct) => periods.GetAsync(publicId, ct);

    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<TaxExemptionPeriodDto> Update(Guid publicId, [FromBody] TaxExemptionPeriodPatchRequest req, CancellationToken ct) => periods.UpdateAsync(publicId, req, ct);

    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<TaxExemptionPeriodDto> Deactivate(Guid publicId, CancellationToken ct) => periods.SetActiveAsync(publicId, false, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.TaxManage)]
    public Task<TaxExemptionPeriodDto> Reactivate(Guid publicId, CancellationToken ct) => periods.SetActiveAsync(publicId, true, ct);
}
