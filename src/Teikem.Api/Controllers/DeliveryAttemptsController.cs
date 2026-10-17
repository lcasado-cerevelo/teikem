using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 93 (P9): intentos de entrega, la fuente de verdad de la liquidación a choferes. Lectura con driverpay.view y escritura con driverpay.manage (módulo LTL_GROUND). Nacen solos del cambio de
/// estatus de la orden a Entregada o Fallida y se pueden registrar o anular a mano; un intento ya liquidado no se anula (409).
/// </summary>
[ApiController]
[Route("api/v1/delivery-attempts")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class DeliveryAttemptsController(DeliveryAttemptService attempts) : ControllerBase
{
    [HttpGet, RequirePermission(PermissionCatalog.DriverPayView)]
    public Task<PagedResult<DeliveryAttemptDto>> List([FromQuery] DeliveryAttemptListQuery query, CancellationToken ct) => attempts.ListAsync(query, ct);

    [HttpPost, RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DeliveryAttemptDto> Create([FromBody] DeliveryAttemptCreateRequest req, CancellationToken ct) => attempts.RecordAsync(req, ct);

    [HttpPost("{publicId:guid}/void"), RequirePermission(PermissionCatalog.DriverPayManage)]
    public Task<DeliveryAttemptDto> Void(Guid publicId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] DeliveryAttemptVoidRequest? req, CancellationToken ct)
        => attempts.VoidAsync(publicId, req?.Reason, ct);
}
