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

/// <summary>404 con el mensaje de «no encontrado» del seguimiento público: es el MISMO para compañía sin seguimiento, número inexistente, número mal escrito u orden dada de baja (sin oráculo).</summary>
public sealed class TrackingNotFoundException() : TeikemException(TrackingRules.NotFoundMessage, 404, "not_found");

/// <summary>
/// Lote 103 — seguimiento público y su ajuste. <b>La consulta pública</b> (<c>GET /api/v1/public/track/{slug}/{waybill}</c>, sin sesión): del número de conduce de una compañía devuelve SOLO el avance (etapa de cuatro,
/// estado y fecha del último movimiento): nada de nombres, direcciones, teléfonos, montos ni números del cliente. <b>El ajuste</b> (<c>/api/v1/tenant/tracking</c>, admin.tenant): la compañía enciende o apaga el
/// seguimiento público poniendo o quitando su identificador corto.
/// </summary>
public sealed class PublicTrackingService(TeikemDbContext db, ITenantContext tenant, OrderProgressService progress)
{
    // ---------------------------------------------------------------- consulta pública

    public async Task<PublicTrackDto> TrackAsync(string? slug, string? waybill, string? lang, CancellationToken ct)
    {
        var s = TrackingRules.Normalize(slug);
        var w = waybill?.Trim().ToUpperInvariant();
        if (s is null || !TrackingRules.IsValidSlug(s) || !TrackingRules.LooksLikeWaybill(w)) throw new TrackingNotFoundException();

        var company = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TrackingSlug == s && t.IsActive).Select(t => new { t.TenantId, t.Name, t.DefaultLangCode }).FirstOrDefaultAsync(ct)
            ?? throw new TrackingNotFoundException();
        var language = lang is "en" or "es" ? lang : company.DefaultLangCode.Trim() == "en" ? "en" : "es";

        // el filtro global de compañía: sin sesión no hay compañía en el principal, así que se trabaja «como» la compañía del identificador (y en el idioma pedido)
        var ctx = (TenantContext)tenant;
        var previousLang = ctx.Lang;
        using (ctx.AsAnonymous(company.TenantId))
        {
            ctx.Lang = language;
            try
            {
                var order = await db.TransportOrders.AsNoTracking().Include(o => o.Status)
                    .Where(o => o.WaybillNumber == w && o.IsActive).Select(o => new { o.TransportOrderId, o.WaybillNumber, Code = o.Status!.InternalCode, o.CreatedAtUtc, o.UpdatedAtUtc })
                    .FirstOrDefaultAsync(ct) ?? throw new TrackingNotFoundException();
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

    // ---------------------------------------------------------------- ajuste de la compañía

    public async Task<TrackingSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var tid = ((TenantContext)tenant).RequireTenantId();
        var slug = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tid).Select(t => t.TrackingSlug).FirstOrDefaultAsync(ct);
        return new TrackingSettingsDto(slug, slug is not null);
    }

    /// <summary>Pone o quita el identificador (vacío = apagar). 400 si no es válido o está reservado; 409 si ya lo usa otra compañía.</summary>
    public async Task<TrackingSettingsDto> SetSettingsAsync(TrackingSettingsRequest req, CancellationToken ct)
    {
        var tid = ((TenantContext)tenant).RequireTenantId();
        var slug = TrackingRules.Normalize(req.Slug);
        if (slug is not null)
        {
            if (!TrackingRules.IsValidSlug(slug)) throw new ValidationException("slug", TrackingRules.InvalidSlugMessage);
            if (TrackingRules.IsReserved(slug)) throw new ValidationException("slug", TrackingRules.ReservedSlugMessage);
            if (await db.Tenants.AsNoTracking().IgnoreQueryFilters().AnyAsync(t => t.TrackingSlug == slug && t.TenantId != tid, ct)) throw new ConflictException(TrackingRules.SlugTakenMessage);
        }
        var row = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.TenantId == tid, ct);
        row.TrackingSlug = slug;
        await db.SaveGuardedAsync(TrackingRules.SlugTakenMessage, ct);
        return new TrackingSettingsDto(slug, slug is not null);
    }
}
