using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Informe "Productos por posición": el DATO del informe (el PDF lo arma el cliente). Mismos filtros que el listado de posiciones
/// (WarehouseLayoutService.FilteredBinRowsAsync: misma validación y mismo 404 de almacén y zona), por código de posición; productos
/// agregados por producto (sin repetir por lote) con existencia en mano &gt; 0, por SKU. take 1..200 (&gt; 200 → 400).
/// La posición (sin TenantId) se alcanza SOLO por su almacén, ya filtrado por tenant.
/// </summary>
public sealed class BinProductsService(TeikemDbContext db)
{
    /// <summary>Posiciones filtradas (una página) con sus productos y el total del filtro.</summary>
    public async Task<BinProductsPageDto> ListAsync(Guid warehousePublicId, BinProductsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Take > BinProductsRules.MaxPerPage) throw new ValidationException("take", BinProductsRules.TakeTooLarge);
        var skip = Math.Max(0, query.Skip);
        var take = query.Take <= 0 ? BinProductsRules.DefaultPerPage : query.Take;

        var generatedAt = DateTime.UtcNow;
        var (w, q) = await WarehouseLayoutService.FilteredBinRowsAsync(db, warehousePublicId, query.Filter ?? new WarehouseBinQuery(), ct);
        var total = await q.CountAsync(ct);
        var page = await q.OrderBy(x => x.Bin.Code).ThenBy(x => x.Bin.WarehouseBinId).Skip(skip).Take(take).ToListAsync(ct);

        var products = await ProductsByBinAsync(w.WarehouseId, page.Select(r => r.Bin.WarehouseBinId).ToList(), ct);
        var items = page.Select(r => new BinProductsDto(r.Bin.WarehouseBinId, r.Bin.Code, r.Bin.WarehouseZoneId, r.ZoneCode, r.Bin.Aisle, r.Bin.Rack,
                r.Bin.Level, r.Bin.Position, r.Bin.IsActive,
                products.TryGetValue(r.Bin.WarehouseBinId, out var list) ? list : Array.Empty<BinProductDto>()))
            .ToList();
        return new BinProductsPageDto(total, skip, take, generatedAt, items);
    }

    /// <summary>
    /// Productos de las posiciones de la página en DOS consultas (sin N+1): pares (posición, producto) con total en mano &gt; 0
    /// sumando lotes, y SKU/nombre/código de barras de esos productos (filtro de tenant). Orden estable: SKU y luego id.
    /// </summary>
    private async Task<Dictionary<int, IReadOnlyList<BinProductDto>>> ProductsByBinAsync(int warehouseId, List<int> binIds, CancellationToken ct)
    {
        if (binIds.Count == 0) return new Dictionary<int, IReadOnlyList<BinProductDto>>();
        var pairs = await ProductPairsQuery(db, warehouseId, binIds).ToListAsync(ct);
        var productIds = pairs.Select(p => p.ProductId).Distinct().ToList();
        var products = productIds.Count == 0
            ? new Dictionary<int, BinProductDto>()
            : (await db.Set<Product>().AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.PublicId, p.Sku, p.Name, p.Barcode })
                .ToListAsync(ct))
              .ToDictionary(p => p.ProductId, p => new BinProductDto(p.PublicId, p.Sku, p.Name, p.Barcode));
        return pairs
            .Where(p => products.ContainsKey(p.ProductId))
            .GroupBy(p => p.BinId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BinProductDto>)g
                .OrderBy(p => products[p.ProductId].Sku, StringComparer.Ordinal).ThenBy(p => p.ProductId)
                .Select(p => products[p.ProductId])
                .ToList());
    }

    /// <summary>Par (posición, producto) con existencia en mano.</summary>
    public sealed class BinProductPair
    {
        public int BinId { get; init; }
        public int ProductId { get; init; }
    }

    /// <summary>
    /// (posición, producto) con total en mano &gt; 0 sumando lotes, de las posiciones dadas del almacén (GROUP BY … HAVING).
    /// Público y estático para probar su traducción a SQL Server sin BD (ToQueryString).
    /// </summary>
    public static IQueryable<BinProductPair> ProductPairsQuery(TeikemDbContext db, int warehouseId, List<int> binIds)
        => db.StockBalances.AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && s.WarehouseBinId != null && binIds.Contains(s.WarehouseBinId.Value) && s.QtyOnHand != 0)
            .GroupBy(s => new { BinId = s.WarehouseBinId!.Value, s.ProductId })
            .Where(g => g.Sum(s => s.QtyOnHand) > 0)
            .Select(g => new BinProductPair { BinId = g.Key.BinId, ProductId = g.Key.ProductId });
}
