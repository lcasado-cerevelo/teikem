using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 94 (P9) — liquidación a choferes (módulo LTL_GROUND): lo que se le paga a cada chofer por sus entregas (con intentos) y viajes en un período. Ver con driverpay.view; generar, revisar, ajustar y
/// cancelar con driverpay.manage; aprobar con driverpay.approve y reautenticación (AAL2). Flujo: generada → revisada → aprobada → exportada. Teikem calcula y exporta; no emite el pago.
/// </summary>
[ApiController]
[Route("api/v1/driver-settlements")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class DriverSettlementsController(DriverSettlementService settlements) : ControllerBase
{
    [HttpGet, RequirePermission(PermissionCatalog.DriverPayView)]
    public Task<PagedResult<DriverSettlementListItemDto>> List([FromQuery] string? status, [FromQuery] Guid? driverPublicId, [FromQuery] string? q, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => settlements.ListAsync(status, driverPublicId, q, skip, take, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.DriverPayView)]
    public Task<DriverSettlementDto> Get(Guid publicId, CancellationToken ct) => settlements.GetAsync(publicId, ct);

    /// <summary>Lo que entraría en la liquidación del período (y del chofer, si se indica); no guarda nada.</summary>
    [HttpPost("preview"), RequirePermission(PermissionCatalog.DriverPayView)]
    public Task<DriverSettlementPreviewDto> Preview([FromBody] DriverSettlementRequest req, CancellationToken ct) => settlements.PreviewAsync(req, ct);

    [HttpPost, RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverSettlementDto> Generate([FromBody] DriverSettlementRequest req, CancellationToken ct) => settlements.GenerateAsync(req, ct);

    [HttpPost("{publicId:guid}/review"), RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverSettlementDto> Review(Guid publicId, CancellationToken ct) => settlements.ReviewAsync(publicId, ct);

    /// <summary>Aprobar exige reautenticación (AAL2), como aprobar una corrida de facturación o una remesa.</summary>
    [HttpPost("{publicId:guid}/approve"), RequirePermission(PermissionCatalog.DriverPayApprove), RequireAal2]
    public Task<DriverSettlementDto> Approve(Guid publicId, CancellationToken ct) => settlements.ApproveAsync(publicId, ct);

    [HttpPost("{publicId:guid}/cancel"), RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverSettlementDto> Cancel(Guid publicId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] DriverSettlementCancelRequest? req, CancellationToken ct)
        => settlements.CancelAsync(publicId, req?.Comment, ct);

    /// <summary>Ajuste manual del monto de una línea (con motivo), mientras la liquidación está generada o revisada.</summary>
    [HttpPatch("{publicId:guid}/lines/{lineId:int}"), RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DriverSettlementDto> AdjustLine(Guid publicId, int lineId, [FromBody] DriverSettlementAdjustRequest req, CancellationToken ct) => settlements.AdjustLineAsync(publicId, lineId, req, ct);
}
