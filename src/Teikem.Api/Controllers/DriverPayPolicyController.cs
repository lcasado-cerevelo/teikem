using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 4 (P6): política de pago a choferes de la compañía (niveles de intento y fórmula vigente) y vista previa del
/// cálculo. Sin fila rigen los valores por defecto (2 niveles, 'Entrega + cada intento').
/// </summary>
[ApiController]
[Route("api/v1/driver-pay-policy")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class DriverPayPolicyController(DriverPayPolicyService policy) : ControllerBase
{
    /// <summary>Política vigente con las fórmulas disponibles.</summary>
    [HttpGet(""), RequirePermission(PermissionCatalog.DriverPayView)]
    public Task<DriverPayPolicyDto> Get(CancellationToken ct) => policy.GetAsync(ct);

    /// <summary>Cambia la fórmula de pago vigente (catálogo DriverPayoutFormula).</summary>
    [HttpPatch(""), RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverPayPolicyDto> Update([FromBody] DriverPayPolicyPatchRequest req, CancellationToken ct)
        => policy.UpdateAsync(req, ct);

    /// <summary>'+ Agregar intento': sube un nivel (tope 20). No crea tarifas.</summary>
    [HttpPost("attempt-levels"), RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverPayPolicyDto> AddAttemptLevel(CancellationToken ct) => policy.AddAttemptLevelAsync(ct);

    /// <summary>Vista previa del pago de una entrega con sus intentos (no escribe nada).</summary>
    [HttpPost("preview"), RequirePermission(PermissionCatalog.DriverPayView)]
    public Task<PayoutPreviewDto> Preview([FromBody] PayoutPreviewRequest req, CancellationToken ct)
        => policy.PreviewAsync(req, ct);
}
