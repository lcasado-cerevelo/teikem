using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 26 (Rentas R0) — "Convertir a serie": pasa un producto sin seguimiento (NONE) que ya tiene movimientos a seguimiento
/// por serie (SERIAL), capturando una serie por cada unidad en mano en cada posición. Es la ÚNICA vía autorizada para cambiar
/// el seguimiento de un producto con movimientos (D25 lo sigue bloqueando en PATCH /products con 409).
/// - Permisos: el controlador pide inventory.manage (cambia el maestro) y el servicio exige además inventory.adjust (mueve
///   inventario); sin él → 403 'Falta el permiso 'inventory.adjust'.' con PERMISSION_DENIED.
/// - Todo en UNA transacción (RunInTransactionAsync), en el orden de bloqueo de ProductService: 1) encabezado Product (U);
///   2) rango de saldos del producto (HOLDLOCK: ningún movimiento entra mientras se decide); 3) verificaciones (seguimiento
///   NONE, nada reservado, sin documentos abiertos, existencia entera por posición sin lote y series = unidades por posición);
///   4) por el ledger (única vía de escritura del inventario): por posición un ADJUSTMENT − del saldo sin serie y un
///   ADJUSTMENT + de 1 por cada serie (nacen AVAILABLE en la posición), con el motivo de sistema TRACKING_CONVERSION; el en mano
///   de cada posición no cambia; 5) TrackingTypeLookupId = SERIAL (Product lleva [AuditEntity]: el cambio queda en AuditLog).
///   Si algo falla, nada se escribe.
/// - Producto sin existencia: solo cambia el seguimiento (sin movimientos).
/// - El TenantId sale del principal: producto, posiciones y saldos se leen bajo el filtro global (las posiciones, hijas sin
///   TenantId, unidas a su almacén filtrado); de otra compañía → 404.
/// </summary>
public sealed class ProductSerialConversionService(TeikemDbContext db, ILookupCache lookups, InventoryLedger ledger, InventoryReadService reads,
    ProductService products, PermissionService permissions)
{
    public async Task<ProductSerialConversionResultDto> ConvertAsync(Guid publicId, ProductSerialConversionRequest req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.InventoryAdjust, ct);
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");

        // 1. Validación pura: notas, posición obligatoria y series normalizadas (sin repetir en toda la solicitud).
        var errors = new Dictionary<string, string[]>();
        var (notes, notesError) = AdjustmentRules.NormalizeNotes(req.Notes);
        if (notesError is not null) errors["notes"] = new[] { notesError };
        var captured = new Dictionary<int, List<string>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var positions = req.Positions ?? Array.Empty<SerialConversionPositionInput>();
        for (var i = 0; i < positions.Count; i++)
        {
            var position = positions[i];
            if (position?.BinId is not int binId)
            {
                errors[$"positions[{i}].binId"] = new[] { AdjustmentRules.BinRequired };
                continue;
            }
            var (serials, serialError) = AdjustmentRules.NormalizeSerials(position.SerialNumbers);
            if (serialError is not null)
            {
                errors[$"positions[{i}].serialNumbers"] = new[] { serialError };
                continue;
            }
            var repeated = serials.FirstOrDefault(s => !seen.Add(s));
            if (repeated is not null)
            {
                errors[$"positions[{i}].serialNumbers"] = new[] { AdjustmentRules.SerialDuplicated(repeated) };
                continue;
            }
            // La misma posición en dos renglones se suma (las series no se repiten: se acaba de verificar).
            if (!captured.TryGetValue(binId, out var list)) captured[binId] = list = new List<string>();
            list.AddRange(serials);
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        var serialTrackingId = await lookups.GetIdAsync(LookupDomains.TrackingType, TrackingTypes.Serial, ct);
        var current = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == publicId, ct)
                      ?? throw new NotFoundException("Producto");
        // Posiciones capturadas: dentro de su almacén filtrado (de otra compañía o inexistente → 404 'Posición no encontrada.').
        var capturedBins = await BinCodesAsync(captured.Keys, ct);
        if (capturedBins.Count != captured.Count) throw new NotFoundException("Posición", feminine: true);

        var keys = new List<BalanceKey>();
        var serialCount = 0;
        var ids = await db.RunInTransactionAsync(async ct2 =>
        {
            keys.Clear();
            serialCount = 0;

            // 2. Encabezado bloqueado (U) y tracked; seguimiento de origen NONE (bajo el bloqueo).
            var product = await db.LockProductAsync(current.ProductId, ct2);
            db.ApplyRowVersion(product, req.RowVersion);
            var trackingCode = (await lookups.GetAsync(product.TrackingTypeLookupId, ct2))?.InternalCode;
            if (SerialConversionRules.CheckSourceTracking(trackingCode, product.Sku) is string trackingError)
                throw new StatusRuleException(trackingError);

            // 3. Rango de saldos del producto (HOLDLOCK) y verificaciones contra lo bloqueado.
            var balances = await db.LockBalancesByProductAsync(product.ProductId, ct2);
            if (balances.Any(b => b.QtyReserved > 0)) throw new ConflictException(SerialConversionRules.ReservedUnits(product.Sku));
            if (await ProductOpenDocuments.AnyAsync(db, product.ProductId, ct2))
                throw new ConflictException(SerialConversionRules.OpenDocuments(product.Sku));

            var withStock = balances.Where(b => b.QtyOnHand != 0m).OrderBy(b => b.StockBalanceId).ToList();
            var stockBins = await BinCodesAsync(withStock.Where(b => b.WarehouseBinId.HasValue).Select(b => b.WarehouseBinId!.Value), ct2);
            foreach (var b in withStock)
            {
                if (b.WarehouseBinId is not int binId || b.LotId is not null)
                {
                    var where = b.WarehouseBinId is int bid
                        ? stockBins.GetValueOrDefault(bid) ?? string.Empty
                        : await db.Set<Warehouse>().AsNoTracking().Where(w => w.WarehouseId == b.WarehouseId).Select(w => w.Code).FirstOrDefaultAsync(ct2) ?? string.Empty;
                    throw new StatusRuleException(SerialConversionRules.StockNotInBin(product.Sku, where));
                }
                if (!SerialConversionRules.IsWholeUnits(b.QtyOnHand))
                    throw new StatusRuleException(SerialConversionRules.FractionalStock(product.Sku, stockBins.GetValueOrDefault(binId) ?? string.Empty, b.QtyOnHand));
            }

            var stock = withStock.Select(b => new SerialConversionStock(b.WarehouseBinId!.Value, stockBins.GetValueOrDefault(b.WarehouseBinId!.Value) ?? string.Empty, b.QtyOnHand)).ToList();
            var mismatch = SerialConversionRules.FirstCountMismatch(stock, captured.ToDictionary(kv => kv.Key, kv => kv.Value.Count), capturedBins);
            if (mismatch is not null) throw new ValidationException("positions", mismatch);

            // 4. Por posición: salida del saldo sin serie y entrada de cada serie (mismo saldo; el en mano no cambia).
            var reason = AdjustmentReasons.TrackingConversion;
            var note = notes ?? SerialConversionRules.DefaultNotes;
            var postings = new List<InventoryPosting>();
            foreach (var b in withStock)
            {
                var binId = b.WarehouseBinId!.Value;
                postings.Add(new InventoryPosting(InventoryTxnTypes.Adjustment, product.ProductId, b.QtyOnHand,
                    FromWarehouseId: b.WarehouseId, FromBinId: binId, ReasonCode: reason, Notes: note));
                foreach (var serial in captured[binId])
                    postings.Add(new InventoryPosting(InventoryTxnTypes.Adjustment, product.ProductId, 1m, SerialNumber: serial,
                        ToWarehouseId: b.WarehouseId, ToBinId: binId, ReasonCode: reason, Notes: note));
                keys.Add(new BalanceKey(product.ProductId, b.WarehouseId, binId, null));
                serialCount += captured[binId].Count;
            }
            var posted = postings.Count == 0 ? Array.Empty<long>() : await ledger.PostAsync(postings, ct2);

            // 5. Seguimiento por serie: única escritura de TrackingTypeLookupId fuera de ProductService (la vigila una prueba).
            product.TrackingTypeLookupId = serialTrackingId;
            await SaveAsync(ct2);
            return posted.ToList();
        }, ct);

        var detail = await products.GetAsync(publicId, InventoryScope.Any, ct);
        var movements = new MovementResultDto(
            ids.Count == 0 ? Array.Empty<KardexRowDto>() : await reads.TransactionsByIdsAsync(ids, ct),
            keys.Count == 0 ? Array.Empty<BalanceDto>() : await reads.BalancesForKeysAsync(keys, ct));
        return new ProductSerialConversionResultDto(detail, serialCount, movements);
    }

    /// <summary>Códigos de posición por id, con la posición unida a su almacén filtrado por tenant.</summary>
    private async Task<Dictionary<int, string>> BinCodesAsync(IEnumerable<int> binIds, CancellationToken ct)
    {
        var ids = binIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();
        return await (from b in db.Set<WarehouseBin>().AsNoTracking()
                      join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                      where ids.Contains(b.WarehouseBinId)
                      select new { b.WarehouseBinId, b.Code }).ToDictionaryAsync(x => x.WarehouseBinId, x => x.Code, ct);
    }

    /// <summary>SaveChanges del cambio de seguimiento: concurrencia (rowVersion) → 409 ConcurrencyMessage.</summary>
    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(DbExtensions.ConcurrencyMessage);
        }
    }
}
