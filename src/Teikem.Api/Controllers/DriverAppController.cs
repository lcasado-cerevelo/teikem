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
public sealed class DriverAppController(DriverAppService app, DriverTrackingService tracking, DriverNoticeService notices) : ControllerBase
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

    /// <summary>Los motivos de «No entregado» (lista «Razón de fallo»).</summary>
    [HttpGet("failure-reasons")]
    public Task<IReadOnlyList<DriverFailureReasonDto>> FailureReasons(CancellationToken ct) => app.ListFailureReasonsAsync(ct);

    /// <summary>Un envío del chofer con contacto, piezas y notas. 404 si no es suyo.</summary>
    [HttpGet("shipments/{publicId:guid}")]
    public Task<DriverShipmentDetailDto> Shipment(Guid publicId, CancellationToken ct) => app.GetShipmentAsync(publicId, ct);

    /// <summary>
    /// Entrega el envío: comprobante (quién recibió, firma y fotos en base64, ubicación) y la orden pasa a Entregada. Idempotente (`alreadyDone`). 400 nombre/firma/fotos; 404 envío ajeno; 409 ya entregado por otra vía;
    /// 422 manifiesto sin aceptar o envío en estatus final.
    /// </summary>
    [HttpPost("shipments/{publicId:guid}/deliver")]
    public Task<DriverShipmentResultDto> Deliver(Guid publicId, [FromBody] DriverDeliverRequest? req, CancellationToken ct) => app.DeliverAsync(publicId, req, ct);

    /// <summary>No entrega el envío con un motivo de «Razón de fallo» (pasa a Fallida y queda el intento). Idempotente. 400 motivo/nota/foto; 404 envío ajeno; 422 manifiesto sin aceptar o estatus que no lo permite.</summary>
    [HttpPost("shipments/{publicId:guid}/fail")]
    public Task<DriverShipmentResultDto> Fail(Guid publicId, [FromBody] DriverFailRequest? req, CancellationToken ct) => app.FailAsync(publicId, req, ct);

    /// <summary>Los valores globales del seguimiento de ruta (distancia mínima, intervalo máximo, cada cuánto se envía, retención).</summary>
    [HttpGet("tracking/config")]
    public DriverTrackingConfigDto TrackingConfig() => tracking.Config();

    /// <summary>
    /// Envía un lote de puntos y avisos del seguimiento (cada 2 minutos). Se guardan ligados a la ruta EN CURSO del chofer en esta compañía; sin ruta en curso solo se guardan los avisos (`routeActive` false).
    /// Idempotente. 400 si pasa de 500 puntos o 50 avisos o un aviso tiene tipo inválido; 403 si no es chofer.
    /// </summary>
    [HttpPost("tracking")]
    public Task<DriverTrackingUploadResultDto> Track([FromBody] DriverTrackingUploadRequest? req, CancellationToken ct) => tracking.UploadAsync(req, ct);

    /// <summary>Su bandeja de avisos (los últimos 100, los más nuevos primero) y cuántos no ha leído.</summary>
    [HttpGet("notices")]
    public Task<DriverNoticesDto> Notices(CancellationToken ct) => notices.ListAsync(ct);

    /// <summary>Marca avisos como leídos: `{ids:[…]}` o `{all:true}`. Devuelve cuántos cambió.</summary>
    [HttpPost("notices/read")]
    public async Task<int> ReadNotices([FromBody] DriverNoticesReadRequest? req, CancellationToken ct) => await notices.MarkReadAsync(req, ct);
}
