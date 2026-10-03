using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 23 — hojas de posición: el papel pegado en el rack con los productos de la posición (SKU, nombre y código de barras).
/// Este servicio da el DATO de las hojas y registra su impresión; el PDF lo arma el cliente (web/app, lotes siguientes).
/// - GET hojas: mismos filtros que el listado de posiciones (WarehouseLayoutService.FilteredBinRowsAsync: misma validación,
///   mismo 404 de almacén y zona, mismo criterio de sheetStatus), por código de posición; productos agregados por producto
///   (sin repetir por lote) con existencia en mano &gt; 0, por SKU. take 1..200 (&gt; 200 → 400).
/// - Marcar impresas: SheetPrintedAtUtc de las posiciones nombradas (todas del almacén o 404, sin escribir nada), con la marca
///   de BinSheetRules.PrintedMark (idempotente; nunca retrocede).
/// La posición (sin TenantId) se alcanza SOLO por su almacén, ya filtrado por tenant: una posición de otro almacén u otro tenant
/// es 404 'Posición no encontrada.'. SheetContentChangedAtUtc NO se escribe aquí: es exclusivo de InventoryLedger.
/// </summary>
public sealed class BinSheetService(TeikemDbContext db)
{
    /// <summary>Hojas de una página de posiciones filtradas, con el total y el staleCount del filtro.</summary>
    public async Task<BinSheetPageDto> ListAsync(Guid warehousePublicId, BinSheetQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Take > BinSheetRules.MaxSheetsPerPage) throw new ValidationException("take", BinSheetRules.TakeTooLarge);
        var skip = Math.Max(0, query.Skip);
        var take = query.Take <= 0 ? BinSheetRules.DefaultSheetsPerPage : query.Take;

        // El instante de los datos se toma ANTES de leer: un cambio que entre después deja la hoja STALE al marcarla impresa.
        var generatedAt = DateTime.UtcNow;
        var (w, q) = await WarehouseLayoutService.FilteredBinRowsAsync(db, warehousePublicId, query.Filter ?? new WarehouseBinQuery(), ct);
        var total = await q.CountAsync(ct);
        var staleCount = total == 0 ? 0 : await q.CountAsync(WarehouseLayoutService.NeedsSheetPrinting, ct);
        var page = await q.OrderBy(x => x.Bin.Code).ThenBy(x => x.Bin.WarehouseBinId).Skip(skip).Take(take).ToListAsync(ct);

        var products = await ProductsByBinAsync(w.WarehouseId, page.Select(r => r.Bin.WarehouseBinId).ToList(), ct);
        var items = page.Select(r => new BinSheetDto(r.Bin.WarehouseBinId, r.Bin.Code, r.Bin.WarehouseZoneId, r.ZoneCode, r.Bin.Aisle, r.Bin.Rack,
                r.Bin.Level, r.Bin.Position, r.Bin.IsActive,
                BinSheetRules.Status(r.OnHand > 0, r.Bin.SheetPrintedAtUtc, r.Bin.SheetContentChangedAtUtc),
                r.Bin.SheetPrintedAtUtc, r.Bin.SheetContentChangedAtUtc,
                products.TryGetValue(r.Bin.WarehouseBinId, out var list) ? list : Array.Empty<BinSheetProductDto>()))
            .ToList();
        return new BinSheetPageDto(total, skip, take, staleCount, generatedAt, items);
    }

    /// <summary>
    /// Marca impresas las hojas de las posiciones nombradas. 400 sin posiciones o con más de 500; 404 si alguna no es del almacén
    /// (o el almacén no es del tenant), sin escribir nada. Devuelve el estado nuevo de cada posición, por código.
    /// </summary>
    public async Task<IReadOnlyList<BinSheetStateDto>> MarkPrintedAsync(Guid warehousePublicId, BinSheetMarkPrintedRequest req, CancellationToken ct)
    {
        var ids = (req?.BinIds ?? Array.Empty<int>()).Distinct().ToList();
        if (ids.Count == 0) throw new ValidationException("binIds", BinSheetRules.MarkPrintedEmpty);
        if (ids.Count > BinSheetRules.MaxMarkPrinted) throw new ValidationException("binIds", BinSheetRules.MarkPrintedTooMany);

        var w = await db.ResolveWarehouseAsync(warehousePublicId, false, ct);
        var bins = await db.WarehouseBins.AsTracking().Where(b => b.WarehouseId == w.WarehouseId && ids.Contains(b.WarehouseBinId)).ToListAsync(ct);
        if (bins.Count != ids.Count) throw WmsResolve.BinNotFound();

        var now = DateTime.UtcNow;
        var generatedAt = AsUtc(req!.GeneratedAtUtc);
        var changed = false;
        foreach (var bin in bins)
        {
            var mark = BinSheetRules.PrintedMark(now, generatedAt, bin.SheetPrintedAtUtc);
            if (bin.SheetPrintedAtUtc == mark) continue;
            bin.SheetPrintedAtUtc = mark;
            changed = true;
        }
        if (changed) await db.SaveChangesAsync(ct);

        var rows = await WarehouseLayoutService.BinRowsQuery(db, w.WarehouseId, new WarehouseBinQuery(IncludeInactive: true, BinIds: ids.ToArray()), null, null)
            .OrderBy(x => x.Bin.Code).ThenBy(x => x.Bin.WarehouseBinId)
            .ToListAsync(ct);
        return rows.Select(r => new BinSheetStateDto(r.Bin.WarehouseBinId, r.Bin.Code,
                BinSheetRules.Status(r.OnHand > 0, r.Bin.SheetPrintedAtUtc, r.Bin.SheetContentChangedAtUtc),
                r.Bin.SheetPrintedAtUtc, r.Bin.SheetContentChangedAtUtc))
            .ToList();
    }

    /// <summary>
    /// Productos de las posiciones de la página en DOS consultas (sin N+1): pares (posición, producto) con total en mano &gt; 0
    /// sumando lotes, y SKU/nombre/código de barras de esos productos (filtro de tenant). Orden estable: SKU y luego id.
    /// </summary>
    private async Task<Dictionary<int, IReadOnlyList<BinSheetProductDto>>> ProductsByBinAsync(int warehouseId, List<int> binIds, CancellationToken ct)
    {
        if (binIds.Count == 0) return new Dictionary<int, IReadOnlyList<BinSheetProductDto>>();
        var pairs = await ProductPairsQuery(db, warehouseId, binIds).ToListAsync(ct);
        var productIds = pairs.Select(p => p.ProductId).Distinct().ToList();
        var products = productIds.Count == 0
            ? new Dictionary<int, BinSheetProductDto>()
            : (await db.Set<Product>().AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.PublicId, p.Sku, p.Name, p.Barcode })
                .ToListAsync(ct))
              .ToDictionary(p => p.ProductId, p => new BinSheetProductDto(p.PublicId, p.Sku, p.Name, p.Barcode));
        return pairs
            .Where(p => products.ContainsKey(p.ProductId))
            .GroupBy(p => p.BinId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BinSheetProductDto>)g
                .OrderBy(p => products[p.ProductId].Sku, StringComparer.Ordinal).ThenBy(p => p.ProductId)
                .Select(p => products[p.ProductId])
                .ToList());
    }

    /// <summary>Par (posición, producto) con existencia en mano en la hoja.</summary>
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

    /// <summary>Instante recibido en JSON como UTC: con zona horaria se convierte; sin ella se toma como UTC.</summary>
    private static DateTime? AsUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        { } v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
    };
}
