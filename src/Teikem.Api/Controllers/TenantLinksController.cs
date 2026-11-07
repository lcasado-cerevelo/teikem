using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 227 (P7 del pedido de vendedores) — Sistema → Integraciones / API → «Compañía enlazada» (módulo INTERCOMPANY). Los enlaces: ver con intercompany.view; proponer, aceptar,
/// rechazar y terminar con intercompany.manage (proponer y aceptar, además, con reautenticación reciente: dan a otra compañía el poder de crear órdenes en la suya o de recibir sus
/// datos). Las entregas por la compañía enlazada (lado de la compañía cliente): verlas con intercompany.view o sales.view; reintentar y revisar ahora con intercompany.manage o
/// warehouse.pick. La compañía sale siempre del principal: un enlace o una entrega ajena «no existe» (404).
/// </summary>
[ApiController]
[Route("api/v1/integrations/tenant-links")]
[Authorize]
[RequireModule(ModuleKeys.Intercompany)]
public sealed class TenantLinksController(TenantLinkService links, TenantLinkBridge bridge) : ControllerBase
{
    private const string ViewDeliveries = PermissionCatalog.IntercompanyView + "|" + PermissionCatalog.SalesView;
    private const string WorkDeliveries = PermissionCatalog.IntercompanyManage + "|" + PermissionCatalog.WarehousePick;

    /// <summary>Para «Proponer enlace»: las otras compañías a las que pertenece quien mira (con o sin el módulo y el Transporte) y si la nuestra tiene Transporte.</summary>
    [HttpGet("options"), RequirePermission(PermissionCatalog.IntercompanyManage)]
    public Task<TenantLinkOptionsDto> Options(CancellationToken ct) => links.OptionsAsync(ct);

    /// <summary>Los enlaces de la compañía (como cliente o como proveedor), del más reciente al más viejo.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.IntercompanyView)]
    public Task<IReadOnlyList<TenantLinkDto>> List(CancellationToken ct) => links.ListAsync(ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.IntercompanyView)]
    public Task<TenantLinkDto> Get(Guid publicId, CancellationToken ct) => links.GetAsync(publicId, ct);

    /// <summary>Proponer: role CLIENT («somos cliente de la otra») o PROVIDER («la otra es nuestra cliente», con clientPublicId). 400/409/422 con el mensaje exacto.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.IntercompanyManage), RequireAal2]
    public Task<TenantLinkDto> Propose([FromBody] TenantLinkProposeRequest? req, CancellationToken ct) => links.ProposeAsync(req, ct);

    /// <summary>Aceptar un enlace que propuso la otra compañía. Si la nuestra da el transporte, clientPublicId (nuestro cliente que la representa) es obligatorio.</summary>
    [HttpPost("{publicId:guid}/accept"), RequirePermission(PermissionCatalog.IntercompanyManage), RequireAal2]
    public Task<TenantLinkDto> Accept(Guid publicId, [FromBody] TenantLinkAcceptRequest? req, CancellationToken ct) => links.AcceptAsync(publicId, req, ct);

    /// <summary>Rechazar (o retirar, si la propusimos) un enlace propuesto. Con motivo.</summary>
    [HttpPost("{publicId:guid}/reject"), RequirePermission(PermissionCatalog.IntercompanyManage)]
    public Task<TenantLinkDto> Reject(Guid publicId, [FromBody] TenantLinkReasonRequest? req, CancellationToken ct) => links.RejectAsync(publicId, req, ct);

    /// <summary>Terminar un enlace activo (cualquiera de las dos compañías). Con motivo. Desde ese momento no cruza nada.</summary>
    [HttpPost("{publicId:guid}/end"), RequirePermission(PermissionCatalog.IntercompanyManage)]
    public Task<TenantLinkDto> End(Guid publicId, [FromBody] TenantLinkReasonRequest? req, CancellationToken ct) => links.EndAsync(publicId, req, ct);

    // ---------------------------------------------------------------- entregas por la compañía enlazada (compañía cliente)

    /// <summary>Las entregas hechas por la compañía enlazada: state = PENDING | CREATED | FAILED; q busca en la orden de venta, el despacho y el número de allá.</summary>
    [HttpGet("deliveries"), RequirePermission(ViewDeliveries)]
    public Task<TenantLinkDeliveryPageDto> Deliveries([FromQuery] string? state, [FromQuery] string? q, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => bridge.ListAsync(state, q, skip, take, ct);

    [HttpGet("deliveries/{publicId:guid}"), RequirePermission(ViewDeliveries)]
    public Task<TenantLinkDeliveryDto> Delivery(Guid publicId, CancellationToken ct) => bridge.GetAsync(publicId, ct);

    /// <summary>Reintentar una entrega que la otra compañía no aceptó (FAILED): se vuelve a mandar al instante.</summary>
    [HttpPost("deliveries/{publicId:guid}/retry"), RequirePermission(WorkDeliveries)]
    public Task<TenantLinkDeliveryDto> Retry(Guid publicId, CancellationToken ct) => bridge.RetryAsync(publicId, ct);

    /// <summary>«Revisar ahora»: trae el estatus de la orden de allá sin esperar al trabajo programado.</summary>
    [HttpPost("deliveries/{publicId:guid}/sync"), RequirePermission(WorkDeliveries)]
    public Task<TenantLinkDeliveryDto> Sync(Guid publicId, CancellationToken ct) => bridge.SyncNowAsync(publicId, ct);

    /// <summary>El comprobante de entrega de la orden de allá (quién recibió, cuándo, firma y fotos), o 204 si todavía no se registra.</summary>
    [HttpGet("deliveries/{publicId:guid}/proof"), RequirePermission(ViewDeliveries)]
    public async Task<ActionResult<DeliveryProofDto>> Proof(Guid publicId, CancellationToken ct)
        => await bridge.ProofAsync(publicId, ct) is { } proof ? proof : NoContent();

    /// <summary>Un archivo del comprobante (firma o foto), en línea. 404 si no es de ese comprobante.</summary>
    [HttpGet("deliveries/{publicId:guid}/proof/files/{filePublicId:guid}"), RequirePermission(ViewDeliveries)]
    public async Task<IActionResult> ProofFile(Guid publicId, Guid filePublicId, CancellationToken ct)
    {
        var f = await bridge.ProofFileAsync(publicId, filePublicId, ct);
        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers.ContentDisposition = $"inline; filename=\"{f.FileName}\"";
        return File(f.Bytes, f.ContentType);
    }
}
