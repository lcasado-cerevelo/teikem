using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// App del chofer (2026-10-23): sus manifiestos, aceptar el manifiesto (la salida) y no aceptar envíos con motivo. Permiso driverapp.use; el usuario debe estar vinculado a un chofer (403 si no).
/// Solo ve lo suyo: un manifiesto de otro chofer es 404. Módulo LTL_GROUND.
/// </summary>
[ApiController]
[Route("api/v1/driver-app")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
[RequirePermission(PermissionCatalog.DriverAppUse)]
public sealed class DriverAppController(DriverAppService app) : ControllerBase
{
    /// <summary>Sus manifiestos de entrega: despachados (por aceptar) y en curso.</summary>
    [HttpGet("manifests")]
    public Task<IReadOnlyList<DriverManifestDto>> Manifests(CancellationToken ct) => app.ListManifestsAsync(ct);

    /// <summary>Un manifiesto con sus envíos en el orden de la ruta.</summary>
    [HttpGet("manifests/{publicId:guid}")]
    public Task<DriverManifestDetailDto> Manifest(Guid publicId, CancellationToken ct) => app.GetManifestAsync(publicId, ct);

    /// <summary>Los motivos para no aceptar un envío (lista editable en Listas).</summary>
    [HttpGet("decline-reasons")]
    public Task<IReadOnlyList<DriverDeclineReasonDto>> Reasons(CancellationToken ct) => app.ListReasonsAsync(ct);

    /// <summary>
    /// Acepta el manifiesto: sale la ruta y los envíos aceptados pasan a En tránsito. Cuerpo opcional {declines:[{shipmentPublicId, reasonCode, note}], rowVersion}: los envíos de `declines` no se aceptan.
    /// 400 motivo faltante o inválido, nota larga, envío repetido o ajeno; 404 manifiesto ajeno; 409 si cambió; 422 si no está despachado o ya se aceptó.
    /// </summary>
    [HttpPost("manifests/{publicId:guid}/accept")]
    public Task<DriverAcceptResultDto> Accept(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DriverAcceptRequest? req, CancellationToken ct)
        => app.AcceptAsync(publicId, req, ct);
}
