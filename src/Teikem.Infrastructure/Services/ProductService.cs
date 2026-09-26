using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P2) — maestro de productos (R23, R24, R26, R31, R32): SKU propio o de un cliente 3PL (dueño = Product.ClientId,
/// nunca el TenantId), unidad base y seguimiento por catálogo, costo/precio, mínimos, posición preferida, lotes y series.
/// - El TenantId sale del principal; todo se lee bajo el filtro global de tenant. Las hijas sin TenantId (lote, serie,
///   posición, zona, línea de recibo) se alcanzan SIEMPRE uniendo con su padre filtrado (Product, Warehouse, ReceiptHeader).
/// - Todas las lecturas reciben InventoryScope (D44): con OwnerClientId solo se ven los productos de ese dueño y los demás
///   dan 404 'Producto no encontrado.' (sin oráculo). Los controladores internos pasan InventoryScope.Any.
/// - Seguimiento, unidad base y dueño son inmutables desde el primer movimiento del ledger (D25): la verificación corre
///   dentro de la transacción con el producto bloqueado y el rango de saldos del producto bloqueado (HOLDLOCK), así que
///   ningún movimiento concurrente se cuela entre la verificación y la escritura.
/// - Desactivar sigue el orden obligatorio del lote: encabezado Product (U) → rango de saldos HOLDLOCK → verificación →
///   escritura. Nunca DELETE.
/// - QtyAvailable (columna computada) nunca se lee: el disponible se calcula en código (en mano − reservado).
/// </summary>
public sealed class ProductService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    /// <summary>Tope de series por consulta (use el buscador para acotar).</summary>
    public const int MaxSerialRows = 1000;
    /// <summary>Unidad base por defecto cuando el alta no la indica.</summary>
    public const string DefaultBaseUom = "UN";

    public static string UnknownUomMessage(string code) => $"Unidad de medida desconocida: '{code}'.";
    public static string UnknownTrackingMessage(string code) => $"Tipo de seguimiento desconocido: '{code}'.";
    public static string UnknownSerialStatusMessage(string code) => $"Estatus de serie desconocido: '{code}'.";

    /// <summary>Campos que el PATCH rechaza aunque lleguen en el cuerpo (van a Extra por no estar en el contrato).</summary>
    private static readonly string[] ImmutableOnPatch = { "sku" };

    /// <summary>Zonas cuyo inventario no cuenta para 'solo con disponible' (selector de recolección, R37; D14).</summary>
    private static readonly string[] NonPickableZoneTypes = { ZoneTypes.Quarantine, ZoneTypes.CrossDock };

    private sealed record Totals(decimal OnHand, decimal Reserved);
    private sealed record OwnerInfo(Guid PublicId, string Name);
    private sealed record PreferredLocation(int? WarehouseId, int? BinId, bool BinIsPicking);

    // ================================================================ lista

    public async Task<ProductPageDto> ListAsync(ProductListQuery q, InventoryScope scope, CancellationToken ct)
    {
        q ??= new ProductListQuery();
        scope ??= InventoryScope.Any;
        var skip = Math.Max(0, q.Skip);
        var take = q.Take <= 0 ? 100 : Math.Min(q.Take, ProductRules.MaxPageSize);

        var query = Scoped(scope).AsNoTracking();
        if (q.ActiveOnly) query = query.Where(p => p.IsActive);
        if (q.OwnOnly == true) query = query.Where(p => p.ClientId == null);
        if (q.OwnerClientPublicId is Guid ownerPublicId)
        {
            var owner = await db.ResolveClientAsync(ownerPublicId, ct);
            query = query.Where(p => p.ClientId == owner.ClientId);
        }
        if (q.CategoryIds is { Length: > 0 })
        {
            // Categoría con sus subcategorías.
            var parents = await CategoryParentsAsync(ct);
            var wanted = ProductRules.WithDescendants(q.CategoryIds, parents).ToList();
            query = query.Where(p => p.ProductCategoryId != null && wanted.Contains(p.ProductCategoryId.Value));
        }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            var clients = db.Set<Client>().AsNoTracking();
            query = query.Where(p => p.Sku.Contains(s) || p.Name.Contains(s) || (p.Barcode != null && p.Barcode.Contains(s))
                                     || clients.Any(c => c.ClientId == p.ClientId && c.Name.Contains(s)));
        }

        int? warehouseId = null;
        if (q.WarehousePublicId is Guid whPublicId) warehouseId = (await ResolveWarehouseAsync(whPublicId, ct)).WarehouseId;

        if (q.OnlyAvailable)
        {
            // Solo productos con disponible recolectable > 0 (sin cuarentena ni cross-dock), en el almacén si se indicó.
            var excludedTypeIds = new List<int>();
            foreach (var code in NonPickableZoneTypes)
                if (await lookups.TryGetIdAsync(LookupDomains.ZoneType, code, ct) is int id) excludedTypeIds.Add(id);
            var excludedBins = from b in db.Set<WarehouseBin>().AsNoTracking()
                               join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                               join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                               where z.ZoneTypeLookupId != null && excludedTypeIds.Contains(z.ZoneTypeLookupId.Value)
                               select b.WarehouseBinId;
            var balances = db.Set<StockBalance>().AsNoTracking()
                .Where(b => (warehouseId == null || b.WarehouseId == warehouseId)
                            && (b.WarehouseBinId == null || !excludedBins.Contains(b.WarehouseBinId.Value)));
            query = query.Where(p => balances.Where(b => b.ProductId == p.ProductId).Sum(b => b.QtyOnHand - b.QtyReserved) > 0);
        }

        var total = await query.CountAsync(ct);
        var page = await query.OrderBy(p => p.Sku).ThenBy(p => p.ProductId).Skip(skip).Take(take).ToListAsync(ct);

        var items = await ToItemsAsync(page, warehouseId, ct);
        return new ProductPageDto(total, skip, take, items);
    }

    // ================================================================ ficha, lotes y series

    public async Task<ProductDetailDto> GetAsync(Guid publicId, InventoryScope scope, CancellationToken ct)
    {
        var p = await ResolveProductAsync(publicId, scope ?? InventoryScope.Any, ct);
        return await ToDetailAsync(p, ct);
    }

    /// <summary>Lotes del producto con su existencia en mano (todas las ubicaciones). Orden FEFO: vencimiento ascendente, sin fecha al final.</summary>
    public async Task<IReadOnlyList<LotDto>> ListLotsAsync(Guid publicId, InventoryScope scope, CancellationToken ct)
    {
        var p = await ResolveProductAsync(publicId, scope ?? InventoryScope.Any, ct);
        // InventoryLot no lleva TenantId: se alcanza por su producto filtrado.
        var lots = await (from l in db.Set<InventoryLot>().AsNoTracking()
                          join pr in db.Set<Product>().AsNoTracking() on l.ProductId equals pr.ProductId
                          where pr.ProductId == p.ProductId
                          select l).ToListAsync(ct);
        if (lots.Count == 0) return Array.Empty<LotDto>();

        var onHand = await db.Set<StockBalance>().AsNoTracking()
            .Where(b => b.ProductId == p.ProductId && b.LotId != null)
            .GroupBy(b => b.LotId!.Value)
            .Select(g => new { LotId = g.Key, Qty = g.Sum(b => b.QtyOnHand) })
            .ToDictionaryAsync(x => x.LotId, x => x.Qty, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return lots
            .OrderBy(l => l.ExpiryDate is null).ThenBy(l => l.ExpiryDate).ThenBy(l => l.LotNumber, StringComparer.OrdinalIgnoreCase)
            .Select(l => new LotDto(l.LotId, l.LotNumber, l.ManufactureDate, l.ExpiryDate, ProductRules.DaysToExpiry(l.ExpiryDate, today),
                onHand.GetValueOrDefault(l.LotId), l.IsActive))
            .ToList();
    }

    /// <summary>
    /// Series del producto con estatus (SerialStatus) y ubicación actual. status acepta 'A,B' (códigos); search busca dentro
    /// del número de serie. Tope MaxSerialRows por número de serie ascendente.
    /// </summary>
    public async Task<IReadOnlyList<SerialDto>> ListSerialsAsync(Guid publicId, string? status, string? search, InventoryScope scope, CancellationToken ct)
    {
        var p = await ResolveProductAsync(publicId, scope ?? InventoryScope.Any, ct);
        var statusMap = await SerialStatusMapAsync(ct);

        // InventorySerial no lleva TenantId: se alcanza por su producto filtrado.
        var query = from s in db.Set<InventorySerial>().AsNoTracking()
                    join pr in db.Set<Product>().AsNoTracking() on s.ProductId equals pr.ProductId
                    where pr.ProductId == p.ProductId
                    select s;

        var codes = (status ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.ToUpperInvariant()).Distinct().ToList();
        if (codes.Count > 0)
        {
            var ids = new List<int>();
            foreach (var code in codes)
            {
                var hit = statusMap.FirstOrDefault(kv => string.Equals(kv.Value.Code, code, StringComparison.OrdinalIgnoreCase));
                if (hit.Value is null) throw new ValidationException("status", UnknownSerialStatusMessage(code));
                ids.Add(hit.Key);
            }
            query = query.Where(s => s.StatusCodeId != null && ids.Contains(s.StatusCodeId.Value));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(s => s.SerialNumber.Contains(term));
        }

        var serials = await query.OrderBy(s => s.SerialNumber).ThenBy(s => s.SerialId).Take(MaxSerialRows).ToListAsync(ct);
        if (serials.Count == 0) return Array.Empty<SerialDto>();

        var lotIds = serials.Where(s => s.LotId.HasValue).Select(s => s.LotId!.Value).Distinct().ToList();
        var lotNumbers = lotIds.Count == 0
            ? new Dictionary<int, string>()
            : await (from l in db.Set<InventoryLot>().AsNoTracking()
                     join pr in db.Set<Product>().AsNoTracking() on l.ProductId equals pr.ProductId
                     where pr.ProductId == p.ProductId && lotIds.Contains(l.LotId)
                     select new { l.LotId, l.LotNumber }).ToDictionaryAsync(x => x.LotId, x => x.LotNumber, ct);

        var whIds = serials.Where(s => s.CurrentWarehouseId.HasValue).Select(s => s.CurrentWarehouseId!.Value).Distinct().ToList();
        var warehouses = whIds.Count == 0
            ? new Dictionary<int, (Guid PublicId, string Code)>()
            : (await db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.WarehouseId))
                .Select(w => new { w.WarehouseId, w.PublicId, w.Code }).ToListAsync(ct))
                .ToDictionary(w => w.WarehouseId, w => (w.PublicId, w.Code));
        var binCodes = await BinCodesAsync(serials.Where(s => s.CurrentBinId.HasValue).Select(s => s.CurrentBinId!.Value), ct);

        return serials.Select(s =>
        {
            var st = s.StatusCodeId is int sid ? statusMap.GetValueOrDefault(sid) : null;
            (Guid PublicId, string Code)? wh = s.CurrentWarehouseId is int wid && warehouses.TryGetValue(wid, out var w) ? w : null;
            return new SerialDto(s.SerialId, s.SerialNumber, s.LotId, s.LotId is int lid ? lotNumbers.GetValueOrDefault(lid) : null,
                st?.Code, st?.Label, wh?.PublicId, wh?.Code, s.CurrentBinId, s.CurrentBinId is int bid ? binCodes.GetValueOrDefault(bid) : null);
        }).ToList();
    }

    // ================================================================ alta

    public async Task<ProductDetailDto> CreateAsync(ProductCreateRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var errors = new Dictionary<string, string[]>();

        var (sku, skuError) = ProductRules.NormalizeSku(req.Sku);
        if (skuError is not null) errors["sku"] = new[] { skuError };
        var (name, nameError) = ProductRules.NormalizeName(req.Name);
        if (nameError is not null) errors["name"] = new[] { nameError };
        var (barcode, barcodeError) = ProductRules.NormalizeBarcode(req.Barcode);
        if (barcodeError is not null) errors["barcode"] = new[] { barcodeError };
        if (ProductRules.Money(req.PurchaseCost, "costo") is string costError) errors["purchaseCost"] = new[] { costError };
        if (ProductRules.Money(req.SalePrice, "precio") is string priceError) errors["salePrice"] = new[] { priceError };
        foreach (var (field, message) in ProductRules.ValidateMeasures(req.WeightKg, req.VolumeM3)) errors[field] = new[] { message };

        var uomCode = string.IsNullOrWhiteSpace(req.BaseUom) ? DefaultBaseUom : req.BaseUom.Trim().ToUpperInvariant();
        var uomId = await lookups.TryGetIdAsync(LookupDomains.UnitOfMeasure, uomCode, ct);
        if (uomId is null) errors["baseUom"] = new[] { UnknownUomMessage(uomCode) };
        var trackingCode = string.IsNullOrWhiteSpace(req.TrackingType) ? TrackingTypes.None : req.TrackingType.Trim().ToUpperInvariant();
        var trackingId = await lookups.TryGetIdAsync(LookupDomains.TrackingType, trackingCode, ct);
        if (trackingId is null) errors["trackingType"] = new[] { UnknownTrackingMessage(trackingCode) };
        if (errors.Count > 0) throw new ValidationException(errors);

        // Dueño (cliente 3PL) activo del tenant; NULL = propio. Categoría activa del tenant. Preferidos del tenant.
        int? ownerId = null;
        if (req.OwnerClientPublicId is Guid ownerPublicId)
            ownerId = ClientQueries.EnsureClientActive(await db.ResolveClientAsync(ownerPublicId, ct)).ClientId;
        int? categoryId = req.CategoryId is int cid ? (await ResolveActiveCategoryAsync(cid, ct)).ProductCategoryId : null;
        var preferred = await ResolvePreferredAsync(req.PreferredWarehousePublicId, req.PreferredBinId, ct);

        var minQty = ZeroAsNull(req.MinQty);
        var minPick = ZeroAsNull(req.MinPickQty);
        var maxPick = ZeroAsNull(req.MaxPickQty);
        var minErrors = ProductRules.ValidateMinimums(minQty, minPick, maxPick, preferred.BinIsPicking);
        if (minErrors.Count > 0) throw new ValidationException(minErrors.ToDictionary(e => e.Field, e => new[] { e.Message }));

        // SKU único por dueño (UQ_Product_Sku sin filtro: también entre inactivos); código de barras único entre activos.
        if (await db.Set<Product>().AnyAsync(p => p.ClientId == ownerId && p.Sku == sku, ct)) throw new ConflictException(ProductRules.SkuTaken);
        if (barcode is not null && await db.Set<Product>().AnyAsync(p => p.IsActive && p.Barcode == barcode, ct))
            throw new ConflictException(ProductRules.BarcodeTaken);

        var product = new Product
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = ownerId,
            Sku = sku!,
            Name = name!,
            ProductCategoryId = categoryId,
            BaseUomLookupId = uomId!.Value,
            TrackingTypeLookupId = trackingId!.Value,
            WeightKg = req.WeightKg,
            VolumeM3 = req.VolumeM3,
            Barcode = barcode,
            PurchaseCost = req.PurchaseCost,
            SalePrice = req.SalePrice,
            PreferredWarehouseId = preferred.WarehouseId,
            PreferredBinId = preferred.BinId,
            MinQty = minQty,
            MinPickQty = minPick,
            MaxPickQty = maxPick,
            IsActive = true,
        };
        db.Set<Product>().Add(product);
        await SaveProductAsync(ct);
        return await GetAsync(product.PublicId, InventoryScope.Any, ct);
    }

    // ================================================================ edición en línea

    /// <summary>
    /// PATCH: null = sin cambio. 'sku' en el cuerpo → 400 (el SKU se fija al crear). Seguimiento, unidad base o dueño con
    /// movimientos → 409 Immutable. Barcode "" o clearBarcode quitan el código; clearOwner = propio; clearCategory y
    /// clearPreferred quitan categoría y preferidos. En los mínimos, 0 = sin mínimo. rowVersion opcional (409 si cambió).
    /// </summary>
    public async Task<ProductDetailDto> UpdateAsync(Guid publicId, ProductPatchRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.Extra is not null)
            foreach (var key in req.Extra.Keys)
            {
                var hit = ImmutableOnPatch.FirstOrDefault(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) throw new ValidationException(hit, ProductRules.SkuImmutable);
            }

        var errors = new Dictionary<string, string[]>();
        string? name = null;
        if (req.Name is not null)
        {
            var (n, nameError) = ProductRules.NormalizeName(req.Name);
            if (nameError is not null) errors["name"] = new[] { nameError };
            name = n;
        }
        string? barcode = null;
        if (req.Barcode is not null)
        {
            var (b, barcodeError) = ProductRules.NormalizeBarcode(req.Barcode);
            if (barcodeError is not null) errors["barcode"] = new[] { barcodeError };
            barcode = b;
        }
        if (ProductRules.Money(req.PurchaseCost, "costo") is string costError) errors["purchaseCost"] = new[] { costError };
        if (ProductRules.Money(req.SalePrice, "precio") is string priceError) errors["salePrice"] = new[] { priceError };
        foreach (var (field, message) in ProductRules.ValidateMeasures(req.WeightKg, req.VolumeM3)) errors[field] = new[] { message };

        int? uomId = null;
        if (!string.IsNullOrWhiteSpace(req.BaseUom))
        {
            var code = req.BaseUom.Trim().ToUpperInvariant();
            uomId = await lookups.TryGetIdAsync(LookupDomains.UnitOfMeasure, code, ct);
            if (uomId is null) errors["baseUom"] = new[] { UnknownUomMessage(code) };
        }
        int? trackingId = null;
        if (!string.IsNullOrWhiteSpace(req.TrackingType))
        {
            var code = req.TrackingType.Trim().ToUpperInvariant();
            trackingId = await lookups.TryGetIdAsync(LookupDomains.TrackingType, code, ct);
            if (trackingId is null) errors["trackingType"] = new[] { UnknownTrackingMessage(code) };
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        // Referencias del tenant resueltas antes de la transacción (lecturas bajo el filtro de tenant).
        int? newOwnerId = null;
        if (req.ClearOwner != true && req.OwnerClientPublicId is Guid ownerPublicId)
            newOwnerId = ClientQueries.EnsureClientActive(await db.ResolveClientAsync(ownerPublicId, ct)).ClientId;
        int? newCategoryId = null;
        if (req.ClearCategory != true && req.CategoryId is int cid) newCategoryId = (await ResolveActiveCategoryAsync(cid, ct)).ProductCategoryId;

        var current = await ResolveProductAsync(publicId, InventoryScope.Any, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            // 1. Encabezado Product bloqueado (U) y tracked.
            var product = await LockProductAsync(current.ProductId, ct2);
            db.ApplyRowVersion(product, req.RowVersion);

            // 2. Inmutables tras el primer movimiento (D25): con el rango de saldos del producto bloqueado (HOLDLOCK),
            //    ningún movimiento nuevo del producto puede entrar entre la verificación y la escritura.
            var ownerTarget = req.ClearOwner == true ? null : newOwnerId ?? product.ClientId;
            var trackingChanges = trackingId is int t && t != product.TrackingTypeLookupId;
            var uomChanges = uomId is int u && u != product.BaseUomLookupId;
            var ownerChanges = ownerTarget != product.ClientId;
            if (trackingChanges || uomChanges || ownerChanges)
            {
                await LockProductBalancesAsync(product.ProductId, ct2);
                var hasMovements = await HasMovementsAsync(product.ProductId, ct2);
                if (ProductRules.ImmutableChange(hasMovements, trackingChanges, uomChanges, ownerChanges) is string field)
                    throw new ConflictException(ProductRules.Immutable(field));
            }

            // 3. Preferidos: clearPreferred quita ambos; almacén sin posición conserva la posición solo si es del nuevo
            //    almacén; posición sin almacén toma el almacén de la posición.
            int? prefWh = product.PreferredWarehouseId, prefBin = product.PreferredBinId;
            if (req.ClearPreferred == true) { prefWh = null; prefBin = null; }
            if (req.PreferredWarehousePublicId is not null || req.PreferredBinId is not null)
            {
                var keepBin = req.PreferredBinId is null && req.PreferredWarehousePublicId is not null ? prefBin : req.PreferredBinId;
                var resolved = await ResolvePreferredAsync(req.PreferredWarehousePublicId, req.PreferredBinId, ct2);
                prefWh = resolved.WarehouseId;
                prefBin = resolved.BinId;
                if (req.PreferredBinId is null && keepBin is int kb && await BinBelongsToAsync(kb, prefWh, ct2)) prefBin = kb;
            }
            var binIsPicking = prefBin is int pb && await IsPickingBinAsync(pb, ct2);

            // 4. Mínimos sobre el estado final (lo actual combinado con lo pedido; 0 = sin mínimo).
            var minQty = req.MinQty.HasValue ? ZeroAsNull(req.MinQty) : product.MinQty;
            var minPick = req.MinPickQty.HasValue ? ZeroAsNull(req.MinPickQty) : product.MinPickQty;
            var maxPick = req.MaxPickQty.HasValue ? ZeroAsNull(req.MaxPickQty) : product.MaxPickQty;
            var minErrors = ProductRules.ValidateMinimums(minQty, minPick, maxPick, binIsPicking);
            if (minErrors.Count > 0) throw new ValidationException(minErrors.ToDictionary(e => e.Field, e => new[] { e.Message }));

            // 5. Unicidad: SKU por dueño (si cambia el dueño) y código de barras entre activos.
            if (ownerChanges && await db.Set<Product>().AnyAsync(p => p.ProductId != product.ProductId && p.ClientId == ownerTarget && p.Sku == product.Sku, ct2))
                throw new ConflictException(ProductRules.SkuTaken);
            var barcodeTarget = req.ClearBarcode == true ? null : req.Barcode is not null ? barcode : product.Barcode;
            if (barcodeTarget is not null && barcodeTarget != product.Barcode && product.IsActive
                && await db.Set<Product>().AnyAsync(p => p.ProductId != product.ProductId && p.IsActive && p.Barcode == barcodeTarget, ct2))
                throw new ConflictException(ProductRules.BarcodeTaken);

            // 6. Escritura.
            if (name is not null) product.Name = name;
            if (trackingId is int tid) product.TrackingTypeLookupId = tid;
            if (uomId is int uid) product.BaseUomLookupId = uid;
            product.ClientId = ownerTarget;
            if (req.ClearCategory == true) product.ProductCategoryId = null;
            else if (newCategoryId is int nc) product.ProductCategoryId = nc;
            if (req.WeightKg.HasValue) product.WeightKg = req.WeightKg;
            if (req.VolumeM3.HasValue) product.VolumeM3 = req.VolumeM3;
            product.Barcode = barcodeTarget;
            if (req.PurchaseCost.HasValue) product.PurchaseCost = req.PurchaseCost;
            if (req.SalePrice.HasValue) product.SalePrice = req.SalePrice;
            product.PreferredWarehouseId = prefWh;
            product.PreferredBinId = prefBin;
            product.MinQty = minQty;
            product.MinPickQty = minPick;
            product.MaxPickQty = maxPick;
            await SaveProductAsync(ct2);
        }, ct);

        return await GetAsync(publicId, InventoryScope.Any, ct);
    }

    // ================================================================ baja y reactivación

    /// <summary>
    /// Desactivar (IsActive = 0), en el orden obligatorio del lote: 1) encabezado Product bloqueado; 2) rango de saldos del
    /// producto HOLDLOCK (ningún movimiento entra mientras se decide); 3) Σ|en mano| &gt; 0 → 409 DeactivateWithStock;
    /// 4) recibos OPEN o tareas abiertas con el producto → 409 DeactivateOpenDocs; 5) IsActive = 0. Ya inactivo: sin cambio.
    /// </summary>
    public async Task<ProductDetailDto> DeactivateAsync(Guid publicId, CancellationToken ct)
    {
        var current = await ResolveProductAsync(publicId, InventoryScope.Any, ct);
        var openReceiptId = await db.StatusIdAsync(StatusDomains.ReceiptStatus, ReceiptStatuses.Open, ct);
        var openTaskIds = new List<int>
        {
            await db.StatusIdAsync(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Pending, ct),
            await db.StatusIdAsync(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.InProgress, ct),
        };

        await db.RunInTransactionAsync(async ct2 =>
        {
            var product = await LockProductAsync(current.ProductId, ct2);
            if (!product.IsActive) return;

            var balances = await LockProductBalancesAsync(product.ProductId, ct2);
            var onHand = balances.Sum(b => Math.Abs(b.QtyOnHand));
            if (onHand > 0) throw new ConflictException(ProductRules.DeactivateWithStock(product.Sku, onHand));

            // ReceiptLine no lleva TenantId: se alcanza por su recibo filtrado.
            var inOpenReceipt = await (from l in db.Set<ReceiptLine>().AsNoTracking()
                                       join h in db.Set<ReceiptHeader>().AsNoTracking() on l.ReceiptHeaderId equals h.ReceiptHeaderId
                                       where l.ProductId == product.ProductId && h.IsActive && h.StatusCodeId == openReceiptId
                                       select l.ReceiptLineId).AnyAsync(ct2);
            var inOpenTask = await db.Set<WarehouseTask>().AsNoTracking()
                .AnyAsync(t => t.ProductId == product.ProductId && openTaskIds.Contains(t.StatusCodeId), ct2);
            if (inOpenReceipt || inOpenTask) throw new ConflictException(ProductRules.DeactivateOpenDocs(product.Sku));

            product.IsActive = false;
            await SaveProductAsync(ct2);
        }, ct);

        return await GetAsync(publicId, InventoryScope.Any, ct);
    }

    /// <summary>Reactivar (IsActive = 1). 409 BarcodeTaken si otro producto activo tomó su código de barras. Ya activo: sin cambio.</summary>
    public async Task<ProductDetailDto> ReactivateAsync(Guid publicId, CancellationToken ct)
    {
        var current = await ResolveProductAsync(publicId, InventoryScope.Any, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var product = await LockProductAsync(current.ProductId, ct2);
            if (product.IsActive) return;
            if (product.Barcode is not null
                && await db.Set<Product>().AnyAsync(p => p.ProductId != product.ProductId && p.IsActive && p.Barcode == product.Barcode, ct2))
                throw new ConflictException(ProductRules.BarcodeTaken);
            product.IsActive = true;
            await SaveProductAsync(ct2);
        }, ct);
        return await GetAsync(publicId, InventoryScope.Any, ct);
    }

    // ================================================================ proyección a DTOs

    private async Task<ProductDetailDto> ToDetailAsync(Product p, CancellationToken ct)
    {
        var item = (await ToItemsAsync(new[] { p }, null, ct))[0];

        Guid? whPublicId = null;
        string? whCode = null;
        if (p.PreferredWarehouseId is int whId)
        {
            var wh = await db.Set<Warehouse>().AsNoTracking().Where(w => w.WarehouseId == whId)
                .Select(w => new { w.PublicId, w.Code }).FirstOrDefaultAsync(ct);
            whPublicId = wh?.PublicId;
            whCode = wh?.Code;
        }
        string? binCode = null;
        if (p.PreferredBinId is int binId) binCode = (await BinCodesAsync(new[] { binId }, ct)).GetValueOrDefault(binId);

        var hasMovements = await HasMovementsAsync(p.ProductId, ct);
        return new ProductDetailDto(item, p.WeightKg, p.VolumeM3, whPublicId, whCode, p.PreferredBinId, binCode,
            p.MinPickQty, p.MaxPickQty, hasMovements, Convert.ToBase64String(p.RowVersion ?? Array.Empty<byte>()));
    }

    /// <summary>Filas de la lista con totales agrupados en UNA consulta (sin N+1); en un almacén si warehouseId.</summary>
    private async Task<IReadOnlyList<ProductListItemDto>> ToItemsAsync(IReadOnlyList<Product> products, int? warehouseId, CancellationToken ct)
    {
        if (products.Count == 0) return Array.Empty<ProductListItemDto>();
        var ids = products.Select(p => p.ProductId).ToList();

        var totals = (await db.Set<StockBalance>().AsNoTracking()
                .Where(b => ids.Contains(b.ProductId) && (warehouseId == null || b.WarehouseId == warehouseId))
                .GroupBy(b => b.ProductId)
                .Select(g => new { ProductId = g.Key, OnHand = g.Sum(b => b.QtyOnHand), Reserved = g.Sum(b => b.QtyReserved) })
                .ToListAsync(ct))
            .ToDictionary(x => x.ProductId, x => new Totals(x.OnHand, x.Reserved));

        var ownerIds = products.Where(p => p.ClientId.HasValue).Select(p => p.ClientId!.Value).Distinct().ToList();
        var owners = ownerIds.Count == 0
            ? new Dictionary<int, OwnerInfo>()
            : (await db.Set<Client>().AsNoTracking().Where(c => ownerIds.Contains(c.ClientId))
                .Select(c => new { c.ClientId, c.PublicId, c.Name }).ToListAsync(ct))
                .ToDictionary(c => c.ClientId, c => new OwnerInfo(c.PublicId, c.Name));

        var categoryIds = products.Where(p => p.ProductCategoryId.HasValue).Select(p => p.ProductCategoryId!.Value).Distinct().ToList();
        var categoryNames = categoryIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Set<ProductCategory>().AsNoTracking().Where(c => categoryIds.Contains(c.ProductCategoryId))
                .ToDictionaryAsync(c => c.ProductCategoryId, c => c.Name, ct);

        var items = new List<ProductListItemDto>(products.Count);
        foreach (var p in products)
        {
            var t = totals.GetValueOrDefault(p.ProductId) ?? new Totals(0m, 0m);
            var available = ProductRules.Available(t.OnHand, t.Reserved);
            var owner = p.ClientId is int oid ? owners.GetValueOrDefault(oid) : null;
            items.Add(new ProductListItemDto(p.ProductId, p.PublicId, p.Sku, p.Name,
                p.ProductCategoryId, p.ProductCategoryId is int cid ? categoryNames.GetValueOrDefault(cid) : null,
                owner?.PublicId, ProductRules.OwnerLabel(owner?.Name), p.ClientId is null,
                await CodeAsync(p.BaseUomLookupId, ct), await CodeAsync(p.TrackingTypeLookupId, ct),
                p.Barcode, p.PurchaseCost, p.SalePrice,
                t.OnHand, t.Reserved, available, p.MinQty, ProductRules.IsBelowMin(p.MinQty, available, p.IsActive), p.IsActive));
        }
        return items;
    }

    // ================================================================ resolución (bajo el filtro de tenant)

    /// <summary>Productos del tenant, acotados al dueño del scope si lo trae (D44).</summary>
    private IQueryable<Product> Scoped(InventoryScope scope)
    {
        var q = db.Set<Product>().AsQueryable();
        if (scope.OwnerClientId is int ownerId) q = q.Where(p => p.ClientId == ownerId);
        return q;
    }

    /// <summary>Producto por PublicId dentro del scope (lectura) o 404 'Producto no encontrado.' (otro tenant u otro dueño: igual).</summary>
    private async Task<Product> ResolveProductAsync(Guid publicId, InventoryScope scope, CancellationToken ct)
        => await Scoped(scope).AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == publicId, ct)
           ?? throw new NotFoundException("Producto");

    private async Task<Warehouse> ResolveWarehouseAsync(Guid publicId, CancellationToken ct)
        => await db.Set<Warehouse>().AsNoTracking().FirstOrDefaultAsync(w => w.PublicId == publicId, ct)
           ?? throw new NotFoundException("Almacén");

    /// <summary>Categoría activa del tenant o 404 'Categoría no encontrada.' / 400 'La categoría está inactiva.'</summary>
    private async Task<ProductCategory> ResolveActiveCategoryAsync(int categoryId, CancellationToken ct)
    {
        var c = await db.Set<ProductCategory>().AsNoTracking().FirstOrDefaultAsync(x => x.ProductCategoryId == categoryId, ct)
                ?? throw new NotFoundException("Categoría", feminine: true);
        if (!c.IsActive) throw new ValidationException("categoryId", ProductRules.CategoryInactive);
        return c;
    }

    /// <summary>
    /// Almacén y posición preferidos. La posición (hija sin TenantId) se resuelve SIEMPRE uniendo con su almacén filtrado:
    /// de otro tenant → 404 'Posición no encontrada.'. Con almacén indicado, una posición de otro almacén → 400
    /// PreferredBinMismatch; sin almacén, se toma el de la posición. Almacén o posición inactivos → 400.
    /// </summary>
    private async Task<PreferredLocation> ResolvePreferredAsync(Guid? warehousePublicId, int? binId, CancellationToken ct)
    {
        int? warehouseId = null;
        if (warehousePublicId is Guid whPublicId)
        {
            var wh = await ResolveWarehouseAsync(whPublicId, ct);
            if (!wh.IsActive) throw new ValidationException("preferredWarehousePublicId", ProductRules.PreferredWarehouseInactive);
            warehouseId = wh.WarehouseId;
        }
        if (binId is not int bid) return new PreferredLocation(warehouseId, null, false);

        var bin = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                         join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                         join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                         where b.WarehouseBinId == bid
                         select new { b.WarehouseBinId, b.WarehouseId, b.IsActive, WarehouseActive = w.IsActive, z.ZoneTypeLookupId })
                     .FirstOrDefaultAsync(ct)
                  ?? throw new NotFoundException("Posición", feminine: true);
        if (warehouseId is int expected && bin.WarehouseId != expected)
            throw new ValidationException("preferredBinId", ProductRules.PreferredBinMismatch);
        if (!bin.WarehouseActive) throw new ValidationException("preferredWarehousePublicId", ProductRules.PreferredWarehouseInactive);
        if (!bin.IsActive) throw new ValidationException("preferredBinId", ProductRules.PreferredBinInactive);
        return new PreferredLocation(bin.WarehouseId, bin.WarehouseBinId, await IsPickingZoneTypeAsync(bin.ZoneTypeLookupId, ct));
    }

    private async Task<bool> BinBelongsToAsync(int binId, int? warehouseId, CancellationToken ct)
        => warehouseId is int wid
           && await (from b in db.Set<WarehouseBin>().AsNoTracking()
                     join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                     where b.WarehouseBinId == binId && b.WarehouseId == wid
                     select b.WarehouseBinId).AnyAsync(ct);

    private async Task<bool> IsPickingBinAsync(int binId, CancellationToken ct)
    {
        var zoneType = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                              join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                              join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                              where b.WarehouseBinId == binId
                              select z.ZoneTypeLookupId).FirstOrDefaultAsync(ct);
        return await IsPickingZoneTypeAsync(zoneType, ct);
    }

    private async Task<bool> IsPickingZoneTypeAsync(int? zoneTypeLookupId, CancellationToken ct)
        => zoneTypeLookupId is int id && (await lookups.GetAsync(id, ct))?.InternalCode == ZoneTypes.Picking;

    /// <summary>Códigos de posición por id (posición unida a su almacén filtrado).</summary>
    private async Task<Dictionary<int, string>> BinCodesAsync(IEnumerable<int> binIds, CancellationToken ct)
    {
        var ids = binIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();
        return await (from b in db.Set<WarehouseBin>().AsNoTracking()
                      join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                      where ids.Contains(b.WarehouseBinId)
                      select new { b.WarehouseBinId, b.Code }).ToDictionaryAsync(x => x.WarehouseBinId, x => x.Code, ct);
    }

    /// <summary>¿El producto tiene al menos un movimiento en el ledger? (Base de los inmutables, D25.)</summary>
    private Task<bool> HasMovementsAsync(int productId, CancellationToken ct)
        => db.Set<InventoryTransaction>().AsNoTracking().AnyAsync(t => t.ProductId == productId, ct);

    private async Task<Dictionary<int, int?>> CategoryParentsAsync(CancellationToken ct)
        => await db.Set<ProductCategory>().AsNoTracking()
            .Select(c => new { c.ProductCategoryId, c.ParentId })
            .ToDictionaryAsync(c => c.ProductCategoryId, c => c.ParentId, ct);

    private sealed record StatusInfo(string Code, string Label);

    /// <summary>Estatus de SerialStatus con la etiqueta personalizada del tenant si existe.</summary>
    private async Task<Dictionary<int, StatusInfo>> SerialStatusMapAsync(CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.SerialStatus).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, StatusCodeOverride>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        return codes.ToDictionary(c => c.StatusCodeId, c => new StatusInfo(c.InternalCode,
            MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, overrides.GetValueOrDefault(c.StatusCodeId)?.CustomLabelJson), tenant.Lang)));
    }

    private async Task<string> CodeAsync(int lookupCodeId, CancellationToken ct)
        => (await lookups.GetAsync(lookupCodeId, ct))?.InternalCode ?? lookupCodeId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>En los mínimos, 0 = sin mínimo (el contrato no trae banderas para quitarlos).</summary>
    private static decimal? ZeroAsNull(decimal? value) => value is 0m ? null : value;

    /// <summary>
    /// SaveChanges con traducción a 409: concurrencia (RowVersion) → ConcurrencyMessage; UX_Product_Barcode → BarcodeTaken;
    /// cualquier otro índice único (UQ_Product_Sku) → SkuTaken.
    /// </summary>
    private async Task SaveProductAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(DbExtensions.ConcurrencyMessage);
        }
        catch (DbUpdateException ex) when (DbExtensions.IsUniqueViolation(ex))
        {
            var barcode = false;
            for (Exception? e = ex; e is not null; e = e.InnerException)
                if (e.Message.Contains("UX_Product_Barcode", StringComparison.OrdinalIgnoreCase)) barcode = true;
            throw new ConflictException(barcode ? ProductRules.BarcodeTaken : ProductRules.SkuTaken);
        }
    }

    // ================================================================ adaptadores a las costuras de P0 (InventoryQueries)
    // Toda dependencia de las firmas de InventoryQueries vive aquí (mismo patrón que WarehouseService de P1).

    /// <summary>Encabezado Product bloqueado (UPDLOCK) y tracked; paso 1 del orden de bloqueo del lote.</summary>
    private Task<Product> LockProductAsync(int productId, CancellationToken ct)
        => db.LockProductAsync(productId, ct);

    /// <summary>Rango de saldos del producto con HOLDLOCK (paso 3 del orden de bloqueo): bloquea también las filas por nacer.</summary>
    private async Task<IReadOnlyList<StockBalance>> LockProductBalancesAsync(int productId, CancellationToken ct)
        => await db.LockBalancesByProductAsync(productId, ct);
}
