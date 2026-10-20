using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 153 — lecturas del portal del cliente de ALMACÉN. Misma regla que el resto del portal: todo se limita al cliente de la sesión (<c>cid</c>) además de la compañía; el cliente solo ve el inventario de SUS
/// productos (Product.ClientId = cid), nunca el propio del operador ni el de otro cliente (sin oráculo: lo ajeno no aparece). Las existencias se suman por producto, lote y almacén: el cliente no ve posiciones.
/// El valor del inventario (la valuación del Lote 148, que sirve al seguro) solo lo ve un acceso ADMIN u OPERADOR del cliente.
/// </summary>
public sealed class PortalWarehouseService(TeikemDbContext db, ITenantContext tenant, InventoryReadService reads, ValuationService valuation, ModuleService modules, ILookupCache lookups)
{
    public const int MaxTake = 100;
    public const string ValueForbidden = "Su acceso no incluye ver el valor del inventario.";

    private int ClientIdOfSession => ((TenantContext)tenant).PortalClientId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);

    private static (int Skip, int Take) Page(int skip, int take) => (Math.Max(0, skip), Math.Clamp(take <= 0 ? 25 : take, 1, MaxTake));

    /// <summary>Lo que la compañía ofrece a este acceso (lo usa /portal/me para armar el menú).</summary>
    public async Task<PortalCapabilitiesDto> CapabilitiesAsync(CancellationToken ct)
    {
        var tc = (TenantContext)tenant;
        var tenantId = tc.TenantId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);
        var company = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var enabled = await modules.GetEnabledKeysAsync(tenantId, ct);
        return new PortalCapabilitiesDto(company, enabled.Contains(ModuleKeys.WmsLotSerial), enabled.Contains(ModuleKeys.LtlGround), await CanSeeValueAsync(ct));
    }

    private async Task<bool> CanSeeValueAsync(CancellationToken ct)
    {
        var tc = (TenantContext)tenant;
        var cid = tc.PortalClientId;
        var userId = tc.UserId;
        if (cid is null || userId is null) return false;
        var role = await (from p in db.PortalUsers.AsNoTracking().IgnoreQueryFilters()
                          join r in db.LookupCodes.AsNoTracking() on p.RoleLookupId equals r.LookupCodeId
                          where p.UserId == userId && p.ClientId == cid && p.TenantId == tc.TenantId && p.IsActive
                          select r.InternalCode).FirstOrDefaultAsync(ct);
        return role is PortalRoles.ClientAdmin or PortalRoles.ClientOperator;
    }

    // ================================================================ existencias

    public async Task<PortalStockPageDto> StockAsync(string? search, Guid? warehousePublicId, int skip, int take, CancellationToken ct)
    {
        var cid = ClientIdOfSession;
        (skip, take) = Page(skip, take);
        var s = search?.Trim();
        var rows = from b in db.Set<StockBalance>().AsNoTracking()
                   join p in db.Set<Product>().AsNoTracking() on b.ProductId equals p.ProductId
                   join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                   where p.ClientId == cid && (b.QtyOnHand != 0 || b.QtyReserved != 0)
                   select new { b, p, w };
        if (warehousePublicId is Guid wp) rows = rows.Where(x => x.w.PublicId == wp);
        if (!string.IsNullOrEmpty(s)) rows = rows.Where(x => x.p.Sku.Contains(s) || x.p.Name.Contains(s) || (x.p.Barcode != null && x.p.Barcode.Contains(s)));

        var grouped = rows.GroupBy(x => new { ProductPublicId = x.p.PublicId, x.p.Sku, ProductName = x.p.Name, WarehousePublicId = x.w.PublicId, WarehouseCode = x.w.Code, WarehouseName = x.w.Name, x.b.LotId })
            .Select(g => new { g.Key.ProductPublicId, g.Key.Sku, g.Key.ProductName, g.Key.WarehousePublicId, g.Key.WarehouseCode, g.Key.WarehouseName, g.Key.LotId, OnHand = g.Sum(x => x.b.QtyOnHand), Reserved = g.Sum(x => x.b.QtyReserved) });
        var total = await grouped.CountAsync(ct);
        var totals = total == 0 ? new { OnHand = 0m, Reserved = 0m } : new { OnHand = await grouped.SumAsync(g => g.OnHand, ct), Reserved = await grouped.SumAsync(g => g.Reserved, ct) };
        var page = await grouped.OrderBy(g => g.WarehouseCode).ThenBy(g => g.Sku).ThenBy(g => g.LotId).Skip(skip).Take(take).ToListAsync(ct);
        var lotIds = page.Where(g => g.LotId != null).Select(g => g.LotId!.Value).Distinct().ToList();
        var lots = lotIds.Count == 0 ? new Dictionary<int, InventoryLot>() : await db.Set<InventoryLot>().AsNoTracking().Where(l => lotIds.Contains(l.LotId)).ToDictionaryAsync(l => l.LotId, ct);
        var items = page.Select(g =>
        {
            var lot = g.LotId is int lid ? lots.GetValueOrDefault(lid) : null;
            return new PortalStockItemDto(g.ProductPublicId, g.Sku, g.ProductName, g.WarehousePublicId, g.WarehouseCode, g.WarehouseName, lot?.LotNumber, lot?.ExpiryDate,
                InventoryRules.Round4(g.OnHand), InventoryRules.Round4(g.Reserved), InventoryRules.Round4(InventoryRules.Available(g.OnHand, g.Reserved)));
        }).ToList();
        return new PortalStockPageDto(total, skip, take, InventoryRules.Round4(totals.OnHand), InventoryRules.Round4(InventoryRules.Available(totals.OnHand, totals.Reserved)), items);
    }

    // ================================================================ productos

    public async Task<PortalProductPageDto> ProductsAsync(string? search, bool includeInactive, int skip, int take, CancellationToken ct)
    {
        var cid = ClientIdOfSession;
        (skip, take) = Page(skip, take);
        var s = search?.Trim();
        var query = db.Set<Product>().AsNoTracking().Where(p => p.ClientId == cid);
        if (!includeInactive) query = query.Where(p => p.IsActive);
        if (!string.IsNullOrEmpty(s)) query = query.Where(p => p.Sku.Contains(s) || p.Name.Contains(s) || (p.Barcode != null && p.Barcode.Contains(s)) || (p.Brand != null && p.Brand.Contains(s)));
        var total = await query.CountAsync(ct);
        var page = await query.OrderBy(p => p.Sku).Skip(skip).Take(take)
            .Select(p => new { p.ProductId, p.PublicId, p.Sku, p.Name, p.Barcode, p.Brand, p.ProductCategoryId, p.TrackingTypeLookupId, p.IsActive }).ToListAsync(ct);
        var ids = page.Select(p => p.ProductId).ToList();
        var stock = (await db.Set<StockBalance>().AsNoTracking().Where(b => ids.Contains(b.ProductId)).Select(b => new { b.ProductId, b.QtyOnHand, b.QtyReserved }).ToListAsync(ct))
            .GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => (OnHand: g.Sum(b => b.QtyOnHand), Reserved: g.Sum(b => b.QtyReserved)));
        var categoryIds = page.Where(p => p.ProductCategoryId != null).Select(p => p.ProductCategoryId!.Value).Distinct().ToList();
        var categories = categoryIds.Count == 0 ? new Dictionary<int, string>() : await db.Set<ProductCategory>().AsNoTracking().Where(c => categoryIds.Contains(c.ProductCategoryId)).ToDictionaryAsync(c => c.ProductCategoryId, c => c.Name, ct);
        var items = new List<PortalProductDto>();
        foreach (var p in page)
        {
            var st = stock.GetValueOrDefault(p.ProductId);
            var tracking = (await lookups.GetAsync(p.TrackingTypeLookupId, ct))?.InternalCode ?? TrackingTypes.None;
            items.Add(new PortalProductDto(p.PublicId, p.Sku, p.Name, p.Barcode, p.Brand, p.ProductCategoryId is int cat ? categories.GetValueOrDefault(cat) : null, tracking, p.IsActive,
                InventoryRules.Round4(st.OnHand), InventoryRules.Round4(InventoryRules.Available(st.OnHand, st.Reserved))));
        }
        return new PortalProductPageDto(total, skip, take, items);
    }

    // ================================================================ movimientos (Kárdex del cliente)

    public async Task<PortalMovementPageDto> MovementsAsync(DateOnly? from, DateOnly? to, string? search, int skip, int take, CancellationToken ct)
    {
        var cid = ClientIdOfSession;
        (skip, take) = Page(skip, take);
        var page = await reads.KardexAsync(new KardexQuery(From: from, To: to, Search: string.IsNullOrWhiteSpace(search) ? null : search.Trim(), Skip: skip, Take: take), new InventoryScope(cid), ct);
        var items = page.Items.Select(r => new PortalMovementDto(r.Id, r.CreatedAtUtc, r.TypeCode, r.Type, r.ProductPublicId, r.Sku, r.ProductName, r.Quantity,
            r.ToWarehouseCode ?? r.FromWarehouseCode, r.LotNumber, r.RefLabel, r.Reason)).ToList();
        return new PortalMovementPageDto(page.Total, page.Skip, page.Take, items);
    }

    // ================================================================ valor del inventario

    public async Task<ValuationReportDto> ValuationAsync(string? search, Guid? warehousePublicId, int skip, int take, CancellationToken ct)
    {
        var cid = ClientIdOfSession;
        if (!await CanSeeValueAsync(ct)) throw new ForbiddenException(ValueForbidden);
        (skip, take) = Page(skip, take);
        var clientPublicId = await db.Set<Client>().AsNoTracking().Where(c => c.ClientId == cid).Select(c => (Guid?)c.PublicId).FirstOrDefaultAsync(ct) ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);
        return await valuation.ReportAsync(warehousePublicId, clientPublicId, search, skip, take, ct);
    }
}
