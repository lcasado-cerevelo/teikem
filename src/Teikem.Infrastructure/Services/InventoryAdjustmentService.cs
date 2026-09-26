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
/// Lote 6 (P3) — ajuste manual de inventario (± con motivo de catálogo, D7) y transferencias entre posiciones o almacenes
/// (una sola fila TRANSFER, D40). Escritura interna del tenant: no recibe InventoryScope (D44).
/// - La ÚNICA vía de escritura es InventoryLedger.PostAsync: este servicio arma los postings con la MAGNITUD y el ledger
///   bloquea los saldos en orden de clave, re-verifica disponible y estado activo, guarda el signo (D3) y mueve las series.
///   Un faltante es 409 insufficient_stock y revierte todo (la operación corre en RunInTransactionAsync).
/// - Almacén: el indicado por PublicId o, si se omite, el único activo del tenant (400 si hay más de uno). La posición se
///   resuelve SIEMPRE dentro de su almacén filtrado: de otro almacén u otro tenant → 404 'Posición no encontrada.'.
/// - Entrada: producto activo (422 si no) y lote asegurado (EnsureLot: el mismo número con otras fechas → 409). Salida: el
///   lote debe existir. Las series de salida o transferencia viajan con su propio lote.
/// - Transferencia: respeta lo reservado (el ledger no deja sacar más que el disponible = en mano − reservado).
/// </summary>
public sealed class InventoryAdjustmentService(TeikemDbContext db, ILookupCache lookups, InventoryLedger ledger, InventoryReadService reads)
{
    public const string WarehouseRequired = "Indique el almacén: la compañía tiene más de uno.";
    public const string NoActiveWarehouse = "La compañía no tiene almacenes activos.";
    public const string WarehouseInactive = "El almacén está inactivo.";
    public static string ProductInactive(string sku) => $"El producto {sku} está inactivo; no admite entradas.";
    public static string SerialOtherLot(string serial) => $"La serie {serial} pertenece a otro lote.";
    public static string LotConcurrent(string lot) => $"El lote {lot} se está registrando en otra operación; intente de nuevo.";

    // ================================================================ ajuste manual

    public async Task<MovementResultDto> AdjustAsync(AdjustmentRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var errors = new Dictionary<string, string[]>();

        var posting = AdjustmentRules.ToPosting(req.Quantity);
        if (!posting.IsValid) errors["quantity"] = new[] { posting.Error! };
        var reasonCatalog = (await lookups.GetDomainAsync(LookupDomains.AdjustmentReason, ct)).Where(l => l.IsActive).Select(l => l.InternalCode);
        var (reasonCode, reasonError) = AdjustmentRules.ValidateReason(req.Reason, reasonCatalog);
        if (reasonError is not null) errors["reason"] = new[] { reasonError };
        var (notes, notesError) = AdjustmentRules.NormalizeNotes(req.Notes);
        if (notesError is not null) errors["notes"] = new[] { notesError };
        var (serials, serialError) = AdjustmentRules.NormalizeSerials(req.SerialNumbers);
        if (serialError is not null) errors["serialNumbers"] = new[] { serialError };
        if (req.ProductPublicId is null) errors["productPublicId"] = new[] { AdjustmentRules.ProductRequired };
        if (req.BinId is null) errors["binId"] = new[] { AdjustmentRules.BinRequired };
        if (req.LotId is not null && req.Lot is not null) errors["lot"] = new[] { AdjustmentRules.LotAmbiguous };
        if (errors.Count > 0) throw new ValidationException(errors);

        var keys = new List<LedgerKey>();
        var ids = await db.RunInTransactionAsync(async ct2 =>
        {
            keys.Clear();
            var product = await ResolveProductAsync(req.ProductPublicId!.Value, ct2);
            if (posting.IsEntry && !product.IsActive) throw new StatusRuleException(ProductInactive(product.Sku));
            var warehouse = await ResolveWarehouseOrDefaultAsync(req.WarehousePublicId, ct2);
            var bin = await ResolveBinAsync(warehouse.WarehouseId, req.BinId!.Value, ct2);
            var tracking = await TrackingOfAsync(product, ct2);

            int? lotId = null;
            if (req.LotId is int requestedLot) lotId = (await ResolveLotAsync(product.ProductId, requestedLot, ct2)).LotId;
            else if (req.Lot is not null)
                lotId = posting.IsEntry
                    ? (await EnsureLotAsync(product.ProductId, req.Lot, ct2)).LotId
                    : (await FindLotByNumberAsync(product.ProductId, req.Lot, ct2)).LotId;

            var serialLots = await SerialLotsAsync(product.ProductId, serials, ct2);
            var hasLot = lotId is not null || serialLots.Values.Any(s => s.LotId is not null);
            if (AdjustmentRules.ValidateTracking(tracking, product.Sku, posting.Magnitude, hasLot, serials.Count) is { } trackingError)
                throw new ValidationException(trackingError.Field, trackingError.Message);

            var postings = new List<InventoryPosting>();
            if (serials.Count > 0)
            {
                foreach (var serial in serials)
                {
                    var (serialId, serialLot) = SerialLotFor(serialLots, serial, lotId);
                    postings.Add(Adjustment(product.ProductId, 1m, posting.IsEntry, warehouse.WarehouseId, bin.WarehouseBinId, serialLot,
                        serialId, serial, reasonCode!, notes));
                    keys.Add(new LedgerKey(product.ProductId, warehouse.WarehouseId, bin.WarehouseBinId, serialLot));
                }
            }
            else
            {
                postings.Add(Adjustment(product.ProductId, posting.Magnitude, posting.IsEntry, warehouse.WarehouseId, bin.WarehouseBinId, lotId,
                    null, null, reasonCode!, notes));
                keys.Add(new LedgerKey(product.ProductId, warehouse.WarehouseId, bin.WarehouseBinId, lotId));
            }
            return await PostAsync(postings, ct2);
        }, ct);

        return new MovementResultDto(await reads.TransactionsByIdsAsync(ids, ct), await reads.BalancesForKeysAsync(keys, ct));
    }

    private static InventoryPosting Adjustment(int productId, decimal magnitude, bool isEntry, int warehouseId, int binId, int? lotId,
        int? serialId, string? serialNumber, string reasonCode, string? notes)
        => isEntry
            ? new InventoryPosting(InventoryTxnTypes.Adjustment, productId, magnitude, LotId: lotId, SerialId: serialId, SerialNumber: serialNumber,
                ToWarehouseId: warehouseId, ToBinId: binId, ReasonCode: reasonCode, Notes: notes)
            : new InventoryPosting(InventoryTxnTypes.Adjustment, productId, magnitude, LotId: lotId, SerialId: serialId, SerialNumber: serialNumber,
                FromWarehouseId: warehouseId, FromBinId: binId, ReasonCode: reasonCode, Notes: notes);

    // ================================================================ transferencia

    public async Task<MovementResultDto> TransferAsync(TransferRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var errors = new Dictionary<string, string[]>();
        if (req.ProductPublicId is null) errors["productPublicId"] = new[] { AdjustmentRules.ProductRequired };
        if (req.Quantity is null) errors["quantity"] = new[] { AdjustmentRules.TransferQuantityRequired };
        if (AdjustmentRules.ValidateTransferBins(req.FromBinId, req.ToBinId) is { } binError) errors[binError.Field] = new[] { binError.Message };
        var (notes, notesError) = AdjustmentRules.NormalizeNotes(req.Notes);
        if (notesError is not null) errors["notes"] = new[] { notesError };
        var (serials, serialError) = AdjustmentRules.NormalizeSerials(req.SerialNumbers);
        if (serialError is not null) errors["serialNumbers"] = new[] { serialError };
        if (errors.Count > 0) throw new ValidationException(errors);

        var magnitude = req.Quantity!.Value;
        var keys = new List<LedgerKey>();
        var ids = await db.RunInTransactionAsync(async ct2 =>
        {
            keys.Clear();
            var product = await ResolveProductAsync(req.ProductPublicId!.Value, ct2);
            var fromWarehouse = await ResolveWarehouseOrDefaultAsync(req.FromWarehousePublicId, ct2);
            var toWarehouse = req.ToWarehousePublicId is Guid toPublicId
                ? await ResolveWarehouseAsync(toPublicId, ct2)
                : fromWarehouse;
            var fromBin = await ResolveBinAsync(fromWarehouse.WarehouseId, req.FromBinId!.Value, ct2);
            var toBin = await ResolveBinAsync(toWarehouse.WarehouseId, req.ToBinId!.Value, ct2);
            var tracking = await TrackingOfAsync(product, ct2);

            int? lotId = req.LotId is int requestedLot ? (await ResolveLotAsync(product.ProductId, requestedLot, ct2)).LotId : null;
            var serialLots = await SerialLotsAsync(product.ProductId, serials, ct2);
            var hasLot = lotId is not null || serialLots.Values.Any(s => s.LotId is not null);
            if (AdjustmentRules.ValidateTracking(tracking, product.Sku, magnitude, hasLot, serials.Count) is { } trackingError)
                throw new ValidationException(trackingError.Field, trackingError.Message);

            var postings = new List<InventoryPosting>();
            void Add(decimal qty, int? lot, int? serialId, string? serialNumber)
            {
                postings.Add(new InventoryPosting(InventoryTxnTypes.Transfer, product.ProductId, qty, LotId: lot, SerialId: serialId,
                    SerialNumber: serialNumber, FromWarehouseId: fromWarehouse.WarehouseId, FromBinId: fromBin.WarehouseBinId,
                    ToWarehouseId: toWarehouse.WarehouseId, ToBinId: toBin.WarehouseBinId, Notes: notes));
                keys.Add(new LedgerKey(product.ProductId, fromWarehouse.WarehouseId, fromBin.WarehouseBinId, lot));
                keys.Add(new LedgerKey(product.ProductId, toWarehouse.WarehouseId, toBin.WarehouseBinId, lot));
            }
            if (serials.Count > 0)
                foreach (var serial in serials)
                {
                    var (serialId, serialLot) = SerialLotFor(serialLots, serial, lotId);
                    Add(1m, serialLot, serialId, serial);
                }
            else Add(magnitude, lotId, null, null);
            return await PostAsync(postings, ct2);
        }, ct);

        return new MovementResultDto(await reads.TransactionsByIdsAsync(ids, ct), await reads.BalancesForKeysAsync(keys, ct));
    }

    // ================================================================ ledger

    /// <summary>Contabiliza en el ledger y devuelve los ids de los movimientos en el orden de los postings.</summary>
    private async Task<IReadOnlyList<long>> PostAsync(IReadOnlyList<InventoryPosting> postings, CancellationToken ct)
    {
        var ids = await ledger.PostAsync(postings, ct);
        return ids.ToList();
    }

    // ================================================================ resolución (bajo el filtro de tenant)
    // Toda la resolución de hijas pasa por su padre filtrado (Warehouse o Product), con los mensajes de WmsResolve.

    private async Task<Product> ResolveProductAsync(Guid publicId, CancellationToken ct)
        => await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == publicId, ct)
           ?? throw new NotFoundException("Producto");

    private async Task<Warehouse> ResolveWarehouseAsync(Guid publicId, CancellationToken ct)
        => await db.Set<Warehouse>().AsNoTracking().FirstOrDefaultAsync(w => w.PublicId == publicId, ct)
           ?? throw new NotFoundException("Almacén");

    /// <summary>El almacén indicado o, si se omite, el único activo del tenant (D26): ninguno o más de uno → 400.</summary>
    private async Task<Warehouse> ResolveWarehouseOrDefaultAsync(Guid? publicId, CancellationToken ct)
    {
        if (publicId is Guid pid) return await ResolveWarehouseAsync(pid, ct);
        var active = await db.Set<Warehouse>().AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.WarehouseId).Take(2).ToListAsync(ct);
        return active.Count switch
        {
            0 => throw new ValidationException("warehousePublicId", NoActiveWarehouse),
            1 => active[0],
            _ => throw new ValidationException("warehousePublicId", WarehouseRequired),
        };
    }

    /// <summary>Posición dentro de su almacén filtrado: de otro almacén u otro tenant → 404 'Posición no encontrada.'.</summary>
    private async Task<WarehouseBin> ResolveBinAsync(int warehouseId, int binId, CancellationToken ct)
        => await (from b in db.Set<WarehouseBin>().AsNoTracking()
                  join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                  where b.WarehouseBinId == binId && b.WarehouseId == warehouseId
                  select b).FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException("Posición", feminine: true);

    /// <summary>Lote del producto (se alcanza por su producto filtrado) o 404 'Lote no encontrado.'.</summary>
    private async Task<InventoryLot> ResolveLotAsync(int productId, int lotId, CancellationToken ct)
        => await (from l in db.Set<InventoryLot>().AsNoTracking()
                  join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                  where l.LotId == lotId && l.ProductId == productId
                  select l).FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException("Lote");

    private async Task<InventoryLot> FindLotByNumberAsync(int productId, LotInput input, CancellationToken ct)
    {
        var (number, error) = AdjustmentRules.ValidateLotInput(input.Number, input.ManufactureDate, input.ExpiryDate);
        if (error is not null) throw new ValidationException("lot", error);
        return await (from l in db.Set<InventoryLot>().AsNoTracking()
                      join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                      where l.ProductId == productId && l.LotNumber == number
                      select l).FirstOrDefaultAsync(ct)
               ?? throw new NotFoundException("Lote");
    }

    /// <summary>
    /// EnsureLot de una entrada: reutiliza el lote con ese número si las fechas capturadas coinciden (409 si difieren, D34);
    /// si no existe lo crea. UQ_Lot (ProductId, LotNumber) es la última línea ante un alta concurrente (409, reintentar).
    /// </summary>
    private async Task<InventoryLot> EnsureLotAsync(int productId, LotInput input, CancellationToken ct)
    {
        var (number, error) = AdjustmentRules.ValidateLotInput(input.Number, input.ManufactureDate, input.ExpiryDate);
        if (error is not null) throw new ValidationException("lot", error);
        var existing = await (from l in db.Set<InventoryLot>().AsNoTracking()
                              join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                              where l.ProductId == productId && l.LotNumber == number
                              select l).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            if (!AdjustmentRules.LotDatesMatch(existing.ManufactureDate, existing.ExpiryDate, input.ManufactureDate, input.ExpiryDate))
                throw new ConflictException(AdjustmentRules.LotExistsWithOtherDates(existing.LotNumber));
            return existing;
        }
        var lot = new InventoryLot
        {
            ProductId = productId,
            LotNumber = number!,
            ManufactureDate = input.ManufactureDate,
            ExpiryDate = input.ExpiryDate,
            IsActive = true,
        };
        db.Set<InventoryLot>().Add(lot);
        await db.SaveGuardedAsync(LotConcurrent(number!), ct);
        return lot;
    }

    private async Task<string> TrackingOfAsync(Product product, CancellationToken ct)
        => (await lookups.GetAsync(product.TrackingTypeLookupId, ct))?.InternalCode ?? TrackingTypes.None;

    private sealed record SerialRef(int SerialId, int? LotId);

    /// <summary>Series existentes del producto por número (sin distinguir mayúsculas), alcanzadas por su producto filtrado.</summary>
    private async Task<Dictionary<string, SerialRef>> SerialLotsAsync(int productId, IReadOnlyList<string> serials, CancellationToken ct)
    {
        var result = new Dictionary<string, SerialRef>(StringComparer.OrdinalIgnoreCase);
        if (serials.Count == 0) return result;
        var numbers = serials.ToList();
        var found = await (from s in db.Set<InventorySerial>().AsNoTracking()
                           join p in db.Set<Product>().AsNoTracking() on s.ProductId equals p.ProductId
                           where s.ProductId == productId && numbers.Contains(s.SerialNumber)
                           select new { s.SerialId, s.SerialNumber, s.LotId }).ToListAsync(ct);
        foreach (var s in found) result[s.SerialNumber] = new SerialRef(s.SerialId, s.LotId);
        return result;
    }

    /// <summary>
    /// Id y lote con que viaja una serie: la existente conserva SU lote (si se indicó otro lote → 400 SerialOtherLot);
    /// una serie nueva (solo en entradas; el ledger la da de alta) toma el lote indicado.
    /// </summary>
    private static (int? SerialId, int? LotId) SerialLotFor(IReadOnlyDictionary<string, SerialRef> known, string serial, int? requestedLot)
    {
        if (!known.TryGetValue(serial, out var s)) return (null, requestedLot);
        if (requestedLot is int rl && s.LotId is int sl && rl != sl) throw new ValidationException("serialNumbers", SerialOtherLot(serial));
        return (s.SerialId, s.LotId ?? requestedLot);
    }
}
