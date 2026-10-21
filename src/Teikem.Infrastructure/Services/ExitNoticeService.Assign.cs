using Microsoft.EntityFrameworkCore;
using Teikem.Domain.ExitNotices;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Lote 171 (B2) — asignar un aviso de salida abierto a quien lo va a surtir, o «tomarlo». Ver <see cref="DocumentAssignmentRules"/>.</summary>
public sealed partial class ExitNoticeService
{
    /// <summary>Asigna (o desasigna con userId null) un aviso de salida solicitado o parcial. Supervisión (warehouse.manage); la persona debe ser miembro activo de la compañía.</summary>
    public async Task<ExitNoticeDto> AssignAsync(Guid publicId, TaskAssignRequest? req, CancellationToken ct)
    {
        var userId = req?.UserId;
        if (userId is int uid) await AssigneeCheck.EnsureActiveMemberAsync(db, uid, ct);
        await SetAssigneeAsync(publicId, userId, takeOnly: false, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>«Tomar» un aviso de salida sin asignar (o ya asignado a quien lo pide). Uno de otra persona se rechaza (409).</summary>
    public async Task<ExitNoticeDto> TakeAsync(Guid publicId, TaskTakeRequest? req, CancellationToken ct)
    {
        await SetAssigneeAsync(publicId, tenant.UserId, takeOnly: true, ct, req?.Origin);
        return await GetAsync(publicId, ct);
    }

    private async Task SetAssigneeAsync(Guid publicId, int? userId, bool takeOnly, CancellationToken ct, string? origin = null)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var notice = await db.Set<ExitNotice>().AsTracking().FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
            var code = (await StatusMapAsync(ct2))[notice.StatusCodeId].Code;
            if (code is not (ExitNoticeStatuses.Requested or ExitNoticeStatuses.Partial)) throw new StatusRuleException(DocumentAssignmentRules.ExitNoticeNotOpen);
            if (takeOnly && !DocumentAssignmentRules.CanTake(notice.AssignedToUserId, tenant.UserId)) throw new ConflictException(DocumentAssignmentRules.AlreadyAssigned);
            notice.AssignedToUserId = userId;
            if (takeOnly) db.AuditNote = DocumentAssignmentRules.TakeNote(origin);
            await db.SaveGuardedAsync(ExitNoticeRules.Concurrency, ct2);
        }, ct);
    }
}
