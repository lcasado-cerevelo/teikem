using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 14 (D5/D6) — filas de "Necesita tu atención" por descuadre Kárdex ↔ saldo ABIERTO (módulo WMS_LOTSERIAL, inventory.view:
/// lo ve quien ve inventario). Una fila por descuadre, los más antiguos primero (DetectedAtUtc), con lo necesario para "Revisar":
/// ruta /warehouse/kardex con tab=reconciliation y discrepancy=&lt;publicId&gt; (la web abre el descuadre). Total = abiertos del
/// tenant para "Ver todos (N)" (misma ruta con status=OPEN). El tenant sale del filtro global (InventoryDiscrepancy es
/// ITenantScoped).
/// Params (cadenas): publicId, kind (BALANCE | PRODUCT_TOTAL), sku, productName, warehouse, bin, lot, where (posición, si no
/// almacén; vacío en el total del producto), ledgerQty, balanceQty, difference (saldo − Kárdex; números con punto decimal),
/// detectedAtUtc (ISO 8601 UTC) y checkCount.
/// </summary>
public sealed class InventoryDiscrepancyAttentionProvider(TeikemDbContext db, ILookupCache lookups) : IAttentionItemProvider
{
    public const string ItemCode = EntityTypes.InventoryDiscrepancy;
    public const string Route = "/warehouse/kardex";
    public const string Tone = "danger";

    public string Code => ItemCode;
    public string BusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.WmsLotSerial;
    public string RequiredPermission => PermissionCatalog.InventoryView;

    public async Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct)
    {
        var open = db.InventoryDiscrepancies.AsNoTracking().Where(d => d.ClosedAtUtc == null);
        var total = await open.CountAsync(ct);
        if (total == 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);

        var rows = await open.OrderBy(d => d.DetectedAtUtc).ThenBy(d => d.InventoryDiscrepancyId).Take(Math.Max(0, take))
            .Select(d => new
            {
                d.PublicId, d.KindLookupId, d.ProductId, d.WarehouseId, d.WarehouseBinId, d.LotId, d.LedgerQty, d.BalanceQty,
                d.DetectedAtUtc, d.CheckCount,
            })
            .ToListAsync(ct);

        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.ProductId))
            .Select(p => new { p.ProductId, p.Sku, p.Name }).ToDictionaryAsync(p => p.ProductId, ct);
        var warehouseIds = rows.Where(r => r.WarehouseId.HasValue).Select(r => r.WarehouseId!.Value).Distinct().ToList();
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.WarehouseId))
            .ToDictionaryAsync(w => w.WarehouseId, w => w.Code, ct);
        var binIds = rows.Where(r => r.WarehouseBinId.HasValue).Select(r => r.WarehouseBinId!.Value).Distinct().ToList();
        var bins = await db.WarehouseBins.AsNoTracking().Where(b => binIds.Contains(b.WarehouseBinId))
            .ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code, ct);
        var lotIds = rows.Where(r => r.LotId.HasValue).Select(r => r.LotId!.Value).Distinct().ToList();
        var lots = await db.InventoryLots.AsNoTracking().Where(l => lotIds.Contains(l.LotId))
            .ToDictionaryAsync(l => l.LotId, l => l.LotNumber, ct);
        var kinds = new Dictionary<int, string>();
        foreach (var id in rows.Select(r => r.KindLookupId).Distinct())
            if (await lookups.GetAsync(id, ct) is { } lc) kinds[id] = lc.InternalCode;

        var items = new List<AttentionItemDto>(rows.Count);
        foreach (var r in rows)
        {
            var product = products.GetValueOrDefault(r.ProductId);
            var warehouse = r.WarehouseId is int w ? warehouses.GetValueOrDefault(w) : null;
            var bin = r.WarehouseBinId is int b ? bins.GetValueOrDefault(b) : null;
            var detected = DateTime.SpecifyKind(r.DetectedAtUtc, DateTimeKind.Utc);
            var publicId = r.PublicId.ToString();
            var parameters = new Dictionary<string, string>
            {
                ["publicId"] = publicId,
                ["kind"] = kinds.GetValueOrDefault(r.KindLookupId) ?? string.Empty,
                ["sku"] = product?.Sku ?? string.Empty,
                ["productName"] = product?.Name ?? string.Empty,
                ["warehouse"] = warehouse ?? string.Empty,
                ["bin"] = bin ?? string.Empty,
                ["lot"] = r.LotId is int l ? lots.GetValueOrDefault(l) ?? string.Empty : string.Empty,
                ["where"] = bin ?? warehouse ?? string.Empty,
                ["ledgerQty"] = Number(r.LedgerQty),
                ["balanceQty"] = Number(r.BalanceQty),
                ["difference"] = Number(ReconciliationRules.Difference(r.LedgerQty, r.BalanceQty)),
                ["detectedAtUtc"] = detected.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                ["checkCount"] = r.CheckCount.ToString(CultureInfo.InvariantCulture),
            };
            items.Add(new AttentionItemDto(ItemCode, BusinessModules.Warehouse, Tone, 1, parameters, Route,
                new Dictionary<string, string> { ["tab"] = "reconciliation", ["discrepancy"] = publicId }, detected));
        }
        var group = new AttentionGroupDto(ItemCode, BusinessModules.Warehouse, total, Route,
            new Dictionary<string, string> { ["tab"] = "reconciliation", ["status"] = InventoryDiscrepancyStatuses.Open });
        return new AttentionProviderResult(total, items, group);
    }

    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
