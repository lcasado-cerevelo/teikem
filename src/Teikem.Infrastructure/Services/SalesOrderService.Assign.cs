using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Lote 195 — asignar una orden de venta solicitada o despachada en parte a quien la va a despachar, o «tomarla». Ver <see cref="DocumentAssignmentRules"/>.</summary>
public sealed partial class SalesOrderService
{
    /// <summary>Asigna (o desasigna con userId null). Supervisión (warehouse.manage); la persona debe ser miembro activo de la compañía.</summary>
    public async Task<SalesOrderDto> AssignAsync(Guid publicId, TaskAssignRequest? req, CancellationToken ct)
    {
        var userId = req?.UserId;
        if (userId is int uid) await AssigneeCheck.EnsureActiveMemberAsync(db, uid, ct);
        await SetAssigneeAsync(publicId, userId, takeOnly: false, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>«Tomar» una orden sin asignar (o ya asignada a quien lo pide). Una de otra persona se rechaza (409).</summary>
    public async Task<SalesOrderDto> TakeAsync(Guid publicId, CancellationToken ct)
    {
        await SetAssigneeAsync(publicId, tenant.UserId, takeOnly: true, ct);
        return await GetAsync(publicId, ct);
    }

    private async Task SetAssigneeAsync(Guid publicId, int? userId, bool takeOnly, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.Set<SalesOrder>().AsTracking().FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
            var code = (await StatusMapAsync(ct2))[order.StatusCodeId].Code;
            if (code is not (SalesOrderStatuses.Requested or SalesOrderStatuses.Partial)) throw new StatusRuleException(DocumentAssignmentRules.SalesOrderNotOpen);
            if (takeOnly && !DocumentAssignmentRules.CanTake(order.AssignedToUserId, tenant.UserId)) throw new ConflictException(DocumentAssignmentRules.AlreadyAssigned);
            order.AssignedToUserId = userId;
            await db.SaveChangesAsync(ct2);
        }, ct);
    }
}
