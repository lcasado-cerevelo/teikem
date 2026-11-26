using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 248 (pieza G) — una lista DEDICADA del Lote 247 (el precio especial de un cliente o de una cadena) tal como la necesitan el aviso «Precio especial por vencer»
/// y su contenido: vigencia, si está activa y publicada, el dueño (y si está activo), cuántos renglones activos tiene y si ya se renovó.
/// </summary>
public sealed record SpecialPriceExpiryRow(
    int PriceListId,
    Guid PublicId,
    string Name,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    bool IsActive,
    bool IsPublished,
    string OwnerKind,
    int OwnerId,
    Guid OwnerPublicId,
    string OwnerName,
    bool OwnerIsActive,
    int ItemCount,
    bool IsRenewed);

/// <summary>Lote 248 (pieza G) — reglas puras del aviso de precio especial.</summary>
public static class SpecialPriceExpiryRules
{
    /// <summary>
    /// ¿Ya se renovó? Hay OTRA lista dedicada activa y publicada del mismo dueño que empieza a más tardar el día siguiente al «hasta» (no deja hueco) y termina después
    /// (o no tiene «hasta»). Las listas que se pasan ya son del mismo dueño, activas y publicadas.
    /// </summary>
    public static bool IsRenewed(int priceListId, DateOnly validTo, IEnumerable<(int Id, DateOnly From, DateOnly? To)> sameOwner)
        => sameOwner.Any(o => o.Id != priceListId && o.From <= validTo.AddDays(1) && (o.To is null || o.To > validTo));

    /// <summary>La ruta de la web de la ficha del dueño: «/catalog/clients?client={id}» o «/catalog/chains?id={id}» (como priceListRules.specialOwnerLink de la web).</summary>
    public static string OwnerLink(string ownerKind, Guid ownerPublicId)
        => ownerKind == SpecialPriceOwnerKinds.Chain ? $"/catalog/chains?id={ownerPublicId:D}" : $"/catalog/clients?client={ownerPublicId:D}";
}

/// <summary>
/// Lote 248 (pieza G) — la ÚNICA lectura de las listas dedicadas para el aviso (la usan la fuente del escaneo y el contenido al enviar, así los dos usan la misma
/// definición de «renovado»). Solo lectura: AsNoTracking y TenantId explícito además del filtro global (corre sin usuario).
/// </summary>
public static class SpecialPriceExpiryQueries
{
    /// <param name="ids">Solo esas listas (null = todas las dedicadas de la compañía).</param>
    /// <param name="onlyLive">true = solo activas y publicadas.</param>
    /// <param name="validToFrom">«Hasta» desde (incluido); con cualquiera de las dos fechas solo salen las que tienen «hasta».</param>
    /// <param name="validToTo">«Hasta» hasta (incluido).</param>
    public static async Task<List<SpecialPriceExpiryRow>> RowsAsync(TeikemDbContext db, int tenantId, IReadOnlyCollection<int>? ids, bool onlyLive,
        DateOnly? validToFrom, DateOnly? validToTo, CancellationToken ct)
    {
        var q = db.Set<PriceList>().AsNoTracking()
            .Where(l => l.TenantId == tenantId && (l.SpecialClientId != null || l.SpecialChainId != null));
        if (ids is not null)
        {
            var wanted = ids.Distinct().ToList();
            q = q.Where(l => wanted.Contains(l.PriceListId));
        }
        if (onlyLive) q = q.Where(l => l.IsActive && l.IsPublished);
        if (validToFrom is DateOnly from) q = q.Where(l => l.ValidTo != null && l.ValidTo >= from);
        if (validToTo is DateOnly to) q = q.Where(l => l.ValidTo != null && l.ValidTo <= to);
        var lists = await q
            .Select(l => new { l.PriceListId, l.PublicId, l.Name, l.ValidFrom, l.ValidTo, l.IsActive, l.IsPublished, l.SpecialClientId, l.SpecialChainId })
            .OrderBy(l => l.ValidTo).ThenBy(l => l.PriceListId)
            .ToListAsync(ct);
        if (lists.Count == 0) return new List<SpecialPriceExpiryRow>();

        var listIds = lists.Select(l => l.PriceListId).ToList();
        var counts = (await db.Set<PriceListItem>().AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.IsActive && listIds.Contains(i.PriceListId))
                .GroupBy(i => i.PriceListId)
                .Select(g => new { PriceListId = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.PriceListId, x => x.Count);

        var clientIds = lists.Where(l => l.SpecialClientId != null).Select(l => l.SpecialClientId!.Value).Distinct().ToList();
        var chainIds = lists.Where(l => l.SpecialChainId != null).Select(l => l.SpecialChainId!.Value).Distinct().ToList();
        var clients = clientIds.Count == 0
            ? new Dictionary<int, (Guid PublicId, string Name, bool IsActive)>()
            : (await db.Clients.AsNoTracking().Where(c => c.TenantId == tenantId && clientIds.Contains(c.ClientId))
                    .Select(c => new { c.ClientId, c.PublicId, c.Name, c.IsActive }).ToListAsync(ct))
                .ToDictionary(c => c.ClientId, c => (c.PublicId, c.Name, c.IsActive));
        var chains = chainIds.Count == 0
            ? new Dictionary<int, (Guid PublicId, string Name, bool IsActive)>()
            : (await db.Set<ClientChain>().AsNoTracking().Where(c => c.TenantId == tenantId && chainIds.Contains(c.ClientChainId))
                    .Select(c => new { c.ClientChainId, c.PublicId, c.Name, c.IsActive }).ToListAsync(ct))
                .ToDictionary(c => c.ClientChainId, c => (c.PublicId, c.Name, c.IsActive));

        // las demás listas dedicadas activas y publicadas de los mismos dueños (para «renovado»)
        var siblings = await db.Set<PriceList>().AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.IsActive && l.IsPublished
                && ((l.SpecialClientId != null && clientIds.Contains(l.SpecialClientId.Value)) || (l.SpecialChainId != null && chainIds.Contains(l.SpecialChainId.Value))))
            .Select(l => new { l.PriceListId, l.ValidFrom, l.ValidTo, l.SpecialClientId, l.SpecialChainId })
            .ToListAsync(ct);

        var rows = new List<SpecialPriceExpiryRow>(lists.Count);
        foreach (var l in lists)
        {
            string kind;
            int ownerId;
            (Guid PublicId, string Name, bool IsActive) owner;
            if (l.SpecialClientId is int clientId)
            {
                (kind, ownerId) = (SpecialPriceOwnerKinds.Client, clientId);
                owner = clients.TryGetValue(clientId, out var c) ? c : (Guid.Empty, string.Empty, false);
            }
            else
            {
                (kind, ownerId) = (SpecialPriceOwnerKinds.Chain, l.SpecialChainId!.Value);
                owner = chains.TryGetValue(ownerId, out var ch) ? ch : (Guid.Empty, string.Empty, false);
            }
            var same = siblings
                .Where(s => kind == SpecialPriceOwnerKinds.Client ? s.SpecialClientId == ownerId : s.SpecialChainId == ownerId)
                .Select(s => (s.PriceListId, s.ValidFrom, s.ValidTo));
            var renewed = l.ValidTo is DateOnly vt && SpecialPriceExpiryRules.IsRenewed(l.PriceListId, vt, same);
            rows.Add(new SpecialPriceExpiryRow(l.PriceListId, l.PublicId, l.Name, l.ValidFrom, l.ValidTo, l.IsActive, l.IsPublished, kind, ownerId, owner.PublicId,
                owner.Name, owner.IsActive, counts.GetValueOrDefault(l.PriceListId), renewed));
        }
        return rows;
    }
}

/// <summary>
/// Lote 248 (pieza G) — fuente SPECIAL_PRICE del trabajo DOCUMENT_EXPIRY_SCAN: los precios especiales (listas dedicadas del Lote 247) activos, publicados y con «hasta»
/// que vencen entre hoy y el mayor umbral (módulo de órdenes de venta, prefijo «SP»). Solo avisa ANTES de vencer o el día que vence: nunca de lo ya vencido
/// (<see cref="NotifyAfterExpiry"/> = false, sin evento de «vencido»). No avisa de lo renovado, de lo que no tiene renglones activos ni de un dueño dado de baja.
/// Las variables salen en el idioma de la compañía y con su formato de fecha (el contenido las vuelve a armar en el idioma de cada destinatario al enviar).
/// </summary>
public sealed class SpecialPriceExpirySource(TeikemDbContext db, ITenantContext tenant) : IExpiryNoticeSource
{
    public const string SourceKey = SpecialPriceCodes.DataSourceKey;

    public string Key => SourceKey;
    public string ModuleKey => ModuleKeys.SalesOrders;
    public string DedupPrefix => DocumentExpiryCodes.SpecialPricePrefix;
    public bool NotifyAfterExpiry => false;

    public async Task<IReadOnlyList<ExpiryCandidate>> CandidatesAsync(DateOnly today, int lookaheadDays, int? lookbackDays, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        // nunca después de vencer: la ventana empieza hoy aunque pidan mirar hacia atrás
        var rows = (await SpecialPriceExpiryQueries.RowsAsync(db, tenantId, null, onlyLive: true, today, today.AddDays(Math.Max(0, lookaheadDays)), ct))
            .Where(r => !r.IsRenewed && r.OwnerIsActive && r.ItemCount > 0 && r.ValidTo is not null)
            .ToList();
        if (rows.Count == 0) return Array.Empty<ExpiryCandidate>();

        var t = await db.Tenants.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        var format = t is null ? TenantFormatRules.Defaults(TenantFormatRules.DefaultRegion) : TenantFormatRules.Read(t);
        var lang = ClientDocumentNoticeText.LangOf(t?.DefaultLangCode);
        return rows.Select(r => new ExpiryCandidate(r.PriceListId, r.ValidTo!.Value, SpecialPriceEventCodes.Expiring, null,
            SpecialPriceNoticeText.BaseValues(r, format, lang).ToDictionary(kv => kv.Key, kv => (string?)kv.Value, StringComparer.Ordinal))).ToList();
    }
}
