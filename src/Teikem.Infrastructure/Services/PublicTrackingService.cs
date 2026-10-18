using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 103/116 — seguimiento público «¿Cómo va mi orden?» por ENLACE: del token propio de la orden (imposible de adivinar, sin sesión) devuelve SOLO el avance (etapa de cuatro, estado y fecha del último movimiento): nada de nombres,
/// direcciones, teléfonos, montos ni números del cliente. Ya no hay búsqueda manual por número de conduce (Lote 116).
/// </summary>
public sealed class PublicTrackingService(TeikemDbContext db, ITenantContext tenant, OrderProgressService progress)
{
    /// <summary>
    /// Lote 116 — el seguimiento por ENLACE (token propio de la orden, imposible de adivinar; sin sesión): devuelve lo mismo que la consulta por número de conduce, solo el avance. Un token que no existe da el mismo 404 genérico.
    /// </summary>
    public async Task<PublicTrackDto> TrackByTokenAsync(string? token, string? lang, CancellationToken ct)
    {
        var t = token?.Trim();
        if (string.IsNullOrEmpty(t) || t.Length > 64) throw new ConsigneeLinkNotFoundException(ConsigneeNoticeRules.TrackLinkNotFoundMessage);
        var found = await db.TransportOrders.AsNoTracking().IgnoreQueryFilters().Where(o => o.TrackToken == t && o.IsActive).Select(o => new { o.TenantId, o.ClientId, o.TransportOrderId }).FirstOrDefaultAsync(ct)
                    ?? throw new ConsigneeLinkNotFoundException(ConsigneeNoticeRules.TrackLinkNotFoundMessage);
        var company = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(x => x.TenantId == found.TenantId && x.IsActive).Select(x => new { x.Name, x.DefaultLangCode }).FirstOrDefaultAsync(ct) ?? throw new ConsigneeLinkNotFoundException(ConsigneeNoticeRules.TrackLinkNotFoundMessage);
        var clientLang = await db.Clients.AsNoTracking().IgnoreQueryFilters().Where(c => c.ClientId == found.ClientId).Select(c => c.NoticeLang).FirstOrDefaultAsync(ct);
        var language = lang is "en" or "es" ? lang : clientLang is "en" or "es" ? clientLang : company.DefaultLangCode.Trim() == "en" ? "en" : "es";
        var ctx = (TenantContext)tenant;
        var previousLang = ctx.Lang;
        using (ctx.AsAnonymous(found.TenantId))
        {
            ctx.Lang = language;
            try
            {
                var order = await db.TransportOrders.AsNoTracking().Include(o => o.Status)
                    .Where(o => o.TransportOrderId == found.TransportOrderId).Select(o => new { o.TransportOrderId, o.WaybillNumber, Code = o.Status!.InternalCode, o.CreatedAtUtc, o.UpdatedAtUtc }).FirstAsync(ct);
                var result = (await progress.ComputeAsync(new[] { new OrderProgressInput(order.TransportOrderId, order.Code) }, ct))[order.TransportOrderId];
                var last = await progress.LastMovementAsync(order.TransportOrderId, ct) ?? order.UpdatedAtUtc ?? order.CreatedAtUtc;
                return Build(order.WaybillNumber, company.Name, result, await progress.StepsAsync(ct), last);
            }
            finally
            {
                ctx.Lang = previousLang;
            }
        }
    }

    /// <summary>Arma la respuesta: las cuatro etapas con cuáles ya pasaron y cuál es la actual.</summary>
    public static PublicTrackDto Build(string waybill, string companyName, OrderProgressResult p, IReadOnlyList<ProgressStatus> steps, DateTime? lastUpdateUtc)
    {
        var cancelled = p.State == OrderProgress.Cancelled;   // cancelada: la barra va apagada (ninguna etapa hecha ni actual)
        var list = steps.Select((s, i) => new PublicTrackStepDto(s.Code, s.Label, !cancelled && (i + 1 < p.Step || (i + 1 == p.Step && p.State == OrderProgress.Completed)), !cancelled && i + 1 == p.Step && p.State != OrderProgress.Completed)).ToList();
        return new PublicTrackDto(waybill, companyName, p.Step, p.Total, p.State, p.StatusLabel, list, lastUpdateUtc);
    }
}
