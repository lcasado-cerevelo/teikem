using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Wms;

/// <summary>
/// Lote 6 (P0, D2) — ÚNICA vía de escritura del inventario. Solo este archivo escribe StockBalance (QtyOnHand y QtyReserved),
/// InventoryTransaction y el estatus/ubicación de InventorySerial (WmsWriteConfinementTests lo verifica por texto).
///
/// - InventoryTransaction es de SOLO INSERCIÓN: una reversa es un movimiento nuevo; Ref y motivo van en el INSERT (D48).
/// - Quantity CON SIGNO (D3): los llamadores pasan la MAGNITUD y el ledger fija el signo (InventoryRules.StoredQuantity).
/// - Una salida sin FromReserved exige disponible (en mano − reservado) ≥ cantidad; con FromReserved exige reservado ≥
///   cantidad y baja reservado y en mano juntos (cross-dock). Si no alcanza → 409 insufficient_stock, sin escribir nada.
/// - Última línea en SQL: CK_StockBalance_Qty (547 → 409 insufficient_stock), CK_InvTxn_Quantity y CK_InvTxn_Direction.
/// - Exige transacción (RunInTransactionAsync) con proveedor relacional; con InMemory valida todo antes de tocar el tracker.
/// - Lote 14: la conciliación (lectura) vive en InventoryReconciler; aquí queda la única corrección de un descuadre,
///   RebuildBalanceAsync (el saldo toma lo que da el Kárdex, sin escribir movimiento).
/// - Lote 14 (P2, D14): al final de PostAsync anota los productos y el mayor movimiento en IInventoryChangeSink; la revisión en
///   segundo plano los recibe solo con el commit real de la transacción (InventoryChangeCommitInterceptor).
/// </summary>
public sealed class InventoryLedger(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StatusService statuses,
    IInventoryChangeSink changes)
{
    public const string ProductOfPostingNotFound = "Producto no encontrado.";
    public const string SerialNotOfProduct = "La serie no pertenece al producto.";

    /// <summary>'La serie {s} no está reservada en {bin}.' (409): liberar o sacar con FromReserved exige la serie RESERVED ahí.</summary>
    public static string SerialNotReserved(string serial, string bin) => $"La serie {serial} no está reservada en {bin}.";

    private sealed record BinInfo(int BinId, int WarehouseId, string Code, bool IsActive);

    private sealed class SerialPlan
    {
        public required int Index { get; init; }
        public required InventoryPosting Posting { get; init; }
        public required string Number { get; init; }
        public InventorySerial? Serial { get; set; }
    }

    // ================================================================ asientos

    /// <summary>
    /// Contabiliza los asientos y devuelve los InventoryTransactionId en el orden de entrada. Pasos: validación de cada asiento
    /// sobre la magnitud (400), claves de saldo ordenadas (upsert + bloqueo), producto/posición/almacén (404/422), series con
    /// bloqueo ascendente (409), deltas contra el saldo bloqueado (409 insufficient_stock), estatus de las series, INSERT de
    /// los movimientos con signo y SaveChanges (547 → 409).
    /// </summary>
    public async Task<IReadOnlyList<long>> PostAsync(IReadOnlyList<InventoryPosting> postings, CancellationToken ct)
    {
        if (postings is null || postings.Count == 0) return Array.Empty<long>();
        InventoryQueries.RequireTransaction(db, nameof(PostAsync));
        var tenantId = RequireTenant();

        // 1. Validación pura sobre la magnitud.
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < postings.Count; i++)
        {
            var p = postings[i];
            var hasSerial = p.SerialId.HasValue || !string.IsNullOrWhiteSpace(p.SerialNumber);
            var msg = InventoryRules.ValidatePosting(p.TxnType, p.FromWarehouseId, p.FromBinId, p.ToWarehouseId, p.ToBinId, p.Quantity, p.ReasonCode, hasSerial);
            if (msg is not null) errors[$"postings[{i}]"] = new[] { msg };
            else if (p.FromReserved && p.FromWarehouseId is null) errors[$"postings[{i}]"] = new[] { InventoryRules.DirectionInvalid(p.TxnType) };
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        // Catálogos (400 si el motivo no existe).
        var txnTypeIds = new Dictionary<string, int>();
        foreach (var t in postings.Select(p => p.TxnType).Distinct())
            txnTypeIds[t] = await lookups.GetIdAsync(LookupDomains.InventoryTxnType, t, ct);
        var reasonIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in postings.Where(p => !string.IsNullOrWhiteSpace(p.ReasonCode)).Select(p => p.ReasonCode!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            reasonIds[r] = await lookups.TryGetIdAsync(LookupDomains.AdjustmentReason, r.ToUpperInvariant(), ct)
                           ?? throw new ValidationException("reason", $"Motivo de ajuste desconocido: '{r}'.");
        var refIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in postings.Where(p => !string.IsNullOrWhiteSpace(p.RefEntityType)).Select(p => p.RefEntityType!).Distinct(StringComparer.OrdinalIgnoreCase))
            refIds[r] = await lookups.GetIdAsync(LookupDomains.EntityType, r, ct);

        // 2-3. Claves de saldo ordenadas: upsert de las que reciben y bloqueo de todas.
        var receiving = new HashSet<BalanceKey>();
        var keys = new SortedSet<BalanceKey>();
        foreach (var p in postings)
        {
            if (p.FromWarehouseId is int fw) keys.Add(new BalanceKey(p.ProductId, fw, p.FromBinId, p.LotId));
            if (p.ToWarehouseId is int tw)
            {
                var k = new BalanceKey(p.ProductId, tw, p.ToBinId, p.LotId);
                keys.Add(k);
                receiving.Add(k);
            }
        }
        var balances = new Dictionary<BalanceKey, StockBalance?>();
        foreach (var k in keys)
        {
            if (receiving.Contains(k)) await db.UpsertBalanceAsync(k, ct);
            balances[k] = await db.LockBalanceAsync(k, ct);
        }

        // 4. Producto, lote, posición y almacén (después de bloquear).
        var productIds = postings.Select(p => p.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.ProductId))
            .Select(p => new { p.ProductId, p.Sku, p.IsActive }).ToDictionaryAsync(p => p.ProductId, ct);
        var lotIds = postings.Where(p => p.LotId.HasValue).Select(p => p.LotId!.Value).Distinct().ToList();
        var lots = await db.InventoryLots.AsNoTracking().Where(l => lotIds.Contains(l.LotId))
            .Select(l => new { l.LotId, l.ProductId }).ToDictionaryAsync(l => l.LotId, ct);
        var warehouseIds = postings.SelectMany(p => new[] { p.FromWarehouseId, p.ToWarehouseId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.WarehouseId))
            .Select(w => new { w.WarehouseId, w.Code, w.IsActive }).ToDictionaryAsync(w => w.WarehouseId, ct);
        var binIds = postings.SelectMany(p => new[] { p.FromBinId, p.ToBinId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var bins = await db.WarehouseBins.AsNoTracking().Where(b => binIds.Contains(b.WarehouseBinId))
            .Select(b => new BinInfo(b.WarehouseBinId, b.WarehouseId, b.Code, b.IsActive)).ToDictionaryAsync(b => b.BinId, ct);

        for (var i = 0; i < postings.Count; i++)
        {
            var p = postings[i];
            if (!products.TryGetValue(p.ProductId, out var product)) throw WmsResolve.ProductNotFound();
            if (p.LotId is int lid && (!lots.TryGetValue(lid, out var lot) || lot.ProductId != p.ProductId)) throw WmsResolve.LotNotFound();
            foreach (var (wh, bin, inbound) in new[] { (p.FromWarehouseId, p.FromBinId, false), (p.ToWarehouseId, p.ToBinId, true) })
            {
                if (wh is not int w) continue;
                if (!warehouses.TryGetValue(w, out var warehouse)) throw WmsResolve.WarehouseNotFound();
                if (bin is int b && (!bins.TryGetValue(b, out var binInfo) || binInfo.WarehouseId != w)) throw WmsResolve.BinNotFound();
                if (!inbound) continue;
                if (!warehouse.IsActive) throw new StatusRuleException(InventoryRules.WarehouseInactiveMessage(warehouse.Code));
                if (bin is int b2 && !bins[b2].IsActive) throw new StatusRuleException(InventoryRules.BinInactiveMessage(bins[b2].Code));
            }
            if (p.ToWarehouseId.HasValue && p.TxnType != InventoryTxnTypes.Transfer && !product.IsActive)
                throw new StatusRuleException(InventoryRules.ProductInactiveMessage(product.Sku));
        }

        // 5. Series: se bloquean por número (SerialId ascendente) y se validan contra el estatus y la ubicación actual.
        var serialPlans = await PlanSerialsAsync(postings, products.ToDictionary(p => p.Key, p => p.Value.Sku), bins, ct);

        // 6. Deltas contra el saldo bloqueado (sin tocar nada hasta validar todo).
        var running = balances.ToDictionary(kv => kv.Key, kv => (OnHand: kv.Value?.QtyOnHand ?? 0m, Reserved: kv.Value?.QtyReserved ?? 0m));
        var shortages = new Dictionary<string, string[]>();
        string? firstShortage = null;
        for (var i = 0; i < postings.Count; i++)
        {
            var p = postings[i];
            if (p.FromWarehouseId is int fw)
            {
                var k = new BalanceKey(p.ProductId, fw, p.FromBinId, p.LotId);
                var (onHand, reserved) = running[k];
                var available = p.FromReserved ? Math.Min(reserved, onHand) : InventoryRules.Available(onHand, reserved);
                if (available < p.Quantity)
                {
                    var where = p.FromBinId is int b ? bins[b].Code : warehouses[fw].Code;
                    var msg = InventoryRules.InsufficientStockMessage(products[p.ProductId].Sku, where, Math.Max(0m, available), p.Quantity);
                    shortages[$"postings[{i}]"] = new[] { msg };
                    firstShortage ??= msg;
                    continue;
                }
                running[k] = (onHand - p.Quantity, p.FromReserved ? reserved - p.Quantity : reserved);
            }
            if (p.ToWarehouseId is int tw)
            {
                var k = new BalanceKey(p.ProductId, tw, p.ToBinId, p.LotId);
                var (onHand, reserved) = running[k];
                running[k] = (onHand + p.Quantity, reserved);
            }
        }
        if (shortages.Count > 0) throw new InsufficientStockException(firstShortage!, shortages);

        // Lote 23: antes/después del en mano de cada clave tocada (antes de aplicar: el saldo bloqueado aún tiene el valor previo).
        var touched = running.Select(kv => (kv.Key, Before: balances[kv.Key]?.QtyOnHand ?? 0m, After: kv.Value.OnHand)).ToList();

        // Aplicación de los saldos (proyección bloqueada del ledger).
        var now = DateTime.UtcNow;
        foreach (var (key, value) in running)
        {
            var balance = balances[key];
            if (balance is null)
            {
                if (value.OnHand == 0m && value.Reserved == 0m) continue;
                balance = new StockBalance
                {
                    TenantId = tenantId, ProductId = key.ProductId, WarehouseId = key.WarehouseId, WarehouseBinId = key.BinId, LotId = key.LotId,
                };
                db.StockBalances.Add(balance);
            }
            if (balance.QtyOnHand == value.OnHand && balance.QtyReserved == value.Reserved) continue;
            balance.QtyOnHand = value.OnHand;
            balance.QtyReserved = value.Reserved;
            balance.UpdatedAtUtc = now;
        }

        // 7. Series: nacimiento, estatus (TransitionAsync INVENTORY_SERIAL) y ubicación actual.
        await ApplySerialsAsync(serialPlans, ct);

        // 8. INSERT de los movimientos con signo y Ref/motivo tal como llegan (sin UPDATE posterior).
        var rows = new List<InventoryTransaction>(postings.Count);
        for (var i = 0; i < postings.Count; i++)
        {
            var p = postings[i];
            var serialId = serialPlans.FirstOrDefault(s => s.Index == i)?.Serial?.SerialId ?? p.SerialId;
            var row = new InventoryTransaction
            {
                TenantId = tenantId,
                TxnTypeLookupId = txnTypeIds[p.TxnType],
                ProductId = p.ProductId,
                LotId = p.LotId,
                SerialId = serialId,
                FromWarehouseId = p.FromWarehouseId,
                FromBinId = p.FromBinId,
                ToWarehouseId = p.ToWarehouseId,
                ToBinId = p.ToBinId,
                Quantity = InventoryRules.StoredQuantity(p.Quantity, p.ToWarehouseId.HasValue),
                RefEntityLookupId = string.IsNullOrWhiteSpace(p.RefEntityType) ? null : refIds[p.RefEntityType!],
                RefId = p.RefId,
                ReasonLookupId = string.IsNullOrWhiteSpace(p.ReasonCode) ? null : reasonIds[p.ReasonCode!.Trim()],
                Notes = string.IsNullOrWhiteSpace(p.Notes) ? null : p.Notes.Trim(),
                CreatedAtUtc = now,
                CreatedBy = tenant.UserId,
            };
            db.InventoryTransactions.Add(row);
            rows.Add(row);
        }

        // 9. Guardado con traducción de la última línea en SQL.
        await SaveAsync(ct);

        // 10. Lote 14 (D14): se anota en la bandeja de la petición qué productos cambiaron; sale a la revisión en segundo plano
        //     solo si la transacción se confirma (InventoryChangeCommitInterceptor).
        var ids = rows.Select(r => r.InventoryTransactionId).ToList();
        changes.Record(tenantId, productIds, ids.Count == 0 ? null : ids.Max());
        return ids;
    }

    // ================================================================ reservas (no escriben movimiento)

    /// <summary>
    /// Reserva saldo en posiciones (cross-dock; D1: olas y Portal lo reutilizarán): en orden de clave, bloquea el saldo y exige
    /// disponible ≥ cantidad (409 insufficient_stock); QtyReserved += cantidad. Reservar no mueve inventario.
    /// </summary>
    public async Task ReserveAsync(IReadOnlyList<StockReservation> reservations, CancellationToken ct)
    {
        if (reservations is null || reservations.Count == 0) return;
        InventoryQueries.RequireTransaction(db, nameof(ReserveAsync));
        RequireTenant();
        await ValidateReservationsAsync(reservations, ct);
        var totals = reservations.GroupBy(r => KeyOf(r)).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
        var locked = new Dictionary<BalanceKey, StockBalance?>();
        foreach (var k in totals.Keys.OrderBy(k => k)) locked[k] = await db.LockBalanceAsync(k, ct);

        var skus = await SkusAsync(totals.Keys.Select(k => k.ProductId), ct);
        var binCodes = await BinCodesAsync(totals.Keys.Select(k => k.BinId), ct);
        var shortages = new Dictionary<string, string[]>();
        string? first = null;
        var i = 0;
        foreach (var (k, qty) in totals.OrderBy(kv => kv.Key))
        {
            var b = locked[k];
            var available = b is null ? 0m : InventoryRules.Available(b.QtyOnHand, b.QtyReserved);
            if (available < qty)
            {
                var msg = InventoryRules.InsufficientStockMessage(skus.GetValueOrDefault(k.ProductId) ?? string.Empty,
                    k.BinId is int bin ? binCodes.GetValueOrDefault(bin) ?? string.Empty : string.Empty, Math.Max(0m, available), qty);
                shortages[$"reservations[{i}]"] = new[] { msg };
                first ??= msg;
            }
            i++;
        }
        if (shortages.Count > 0) throw new InsufficientStockException(first!, shortages);

        // Series (L328): AVAILABLE en la posición → RESERVED, con bloqueo ascendente después de los saldos.
        var serials = await LockReservationSerialsAsync(reservations, binCodes, SerialStatuses.Available, ct);

        var now = DateTime.UtcNow;
        foreach (var (k, qty) in totals)
        {
            var b = locked[k]!;
            b.QtyReserved += qty;
            b.UpdatedAtUtc = now;
        }
        await TransitionSerialsAsync(serials, SerialStatuses.Reserved, ct);
        await SaveAsync(ct);
    }

    /// <summary>Libera reserva: QtyReserved −= cantidad; si quedara &lt; 0 → 409 'La reserva a liberar excede lo reservado.'.</summary>
    public async Task ReleaseAsync(IReadOnlyList<StockReservation> reservations, CancellationToken ct)
    {
        if (reservations is null || reservations.Count == 0) return;
        InventoryQueries.RequireTransaction(db, nameof(ReleaseAsync));
        RequireTenant();
        await ValidateReservationsAsync(reservations, ct);
        var totals = reservations.GroupBy(r => KeyOf(r)).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
        var locked = new Dictionary<BalanceKey, StockBalance?>();
        foreach (var k in totals.Keys.OrderBy(k => k)) locked[k] = await db.LockBalanceAsync(k, ct);
        foreach (var (k, qty) in totals)
            if (locked[k] is not { } b || b.QtyReserved < qty) throw new ConflictException(InventoryRules.ReleaseExceedsReserved);

        // Series (L328): RESERVED en la posición → AVAILABLE.
        var binCodes = await BinCodesAsync(totals.Keys.Select(k => k.BinId), ct);
        var serials = await LockReservationSerialsAsync(reservations, binCodes, SerialStatuses.Reserved, ct);

        var now = DateTime.UtcNow;
        foreach (var (k, qty) in totals)
        {
            var b = locked[k]!;
            b.QtyReserved -= qty;
            b.UpdatedAtUtc = now;
        }
        await TransitionSerialsAsync(serials, SerialStatuses.Available, ct);
        await SaveAsync(ct);
    }

    // ================================================================ reconstrucción del saldo (Lote 14, D5)

    /// <summary>
    /// Lote 14 (D5) — "Corregir el saldo según el Kárdex": el saldo de la clave toma lo que dan sus movimientos. Es la única
    /// corrección de un descuadre Kárdex ↔ saldo (un ajuste o un conteo mueven los dos por igual y no lo arreglan).
    /// - Exige transacción; asegura la fila (upsert con UPDLOCK + HOLDLOCK) y la bloquea, y SOLO ENTONCES suma el Kárdex de la
    ///   clave (To +|Q|, From −|Q|): con la clave bloqueada ningún movimiento de esa clave puede intercalarse.
    /// - Kárdex negativo o menor que lo reservado → 409 sin escribir nada (CK_StockBalance_Qty lo rechazaría igual).
    /// - No escribe InventoryTransaction: el Kárdex manda; cambia QtyOnHand y UpdatedAtUtc (lo reservado no se toca).
    /// Devuelve el en mano antes y después (iguales = ya cuadraba) y lo reservado.
    /// </summary>
    public async Task<BalanceRebuildResult> RebuildBalanceAsync(BalanceKey key, CancellationToken ct)
    {
        InventoryQueries.RequireTransaction(db, nameof(RebuildBalanceAsync));
        var tenantId = RequireTenant();
        await db.UpsertBalanceAsync(key, ct);
        var balance = await db.LockBalanceAsync(key, ct);

        var (p, w, b, l) = (key.ProductId, key.WarehouseId, key.BinId, key.LotId);
        var inbound = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.ProductId == p && t.ToWarehouseId == w && t.ToBinId == b && t.LotId == l)
            .SumAsync(t => Math.Abs(t.Quantity), ct);
        var outbound = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.ProductId == p && t.FromWarehouseId == w && t.FromBinId == b && t.LotId == l)
            .SumAsync(t => Math.Abs(t.Quantity), ct);
        var ledgerQty = inbound - outbound;

        var before = balance?.QtyOnHand ?? 0m;
        var reserved = balance?.QtyReserved ?? 0m;
        if (ledgerQty == before) return new BalanceRebuildResult(before, before, reserved);

        var sku = (await SkusAsync(new[] { p }, ct)).GetValueOrDefault(p) ?? string.Empty;
        var where = b is int binId
            ? (await BinCodesAsync(new int?[] { binId }, ct)).GetValueOrDefault(binId) ?? string.Empty
            : await db.Warehouses.AsNoTracking().Where(x => x.WarehouseId == w).Select(x => x.Code).FirstOrDefaultAsync(ct) ?? string.Empty;
        var error = ReconciliationRules.RebuildCheck(ledgerQty, reserved, sku, where);
        if (error is not null) throw new ConflictException(error);

        if (balance is null)
        {
            balance = new StockBalance { TenantId = tenantId, ProductId = p, WarehouseId = w, WarehouseBinId = b, LotId = l };
            db.StockBalances.Add(balance);
        }
        balance.QtyOnHand = ledgerQty;
        var now = DateTime.UtcNow;
        balance.UpdatedAtUtc = now;
        await SaveAsync(ct);
        return new BalanceRebuildResult(before, ledgerQty, reserved);
    }

    // ================================================================ series

    private async Task<List<SerialPlan>> PlanSerialsAsync(IReadOnlyList<InventoryPosting> postings, IReadOnlyDictionary<int, string> skus,
        IReadOnlyDictionary<int, BinInfo> bins, CancellationToken ct)
    {
        var plans = new List<SerialPlan>();
        var byId = postings.Where(p => p.SerialId.HasValue && string.IsNullOrWhiteSpace(p.SerialNumber)).Select(p => p.SerialId!.Value).Distinct().ToList();
        var numbersById = byId.Count == 0
            ? new Dictionary<int, (int ProductId, string Number)>()
            : (await (from s in db.InventorySerials.AsNoTracking()
                      join pr in db.Products.AsNoTracking() on s.ProductId equals pr.ProductId
                      where byId.Contains(s.SerialId)
                      select new { s.SerialId, s.ProductId, s.SerialNumber }).ToListAsync(ct))
              .ToDictionary(s => s.SerialId, s => (ProductId: s.ProductId, Number: s.SerialNumber));
        for (var i = 0; i < postings.Count; i++)
        {
            var p = postings[i];
            string? number = p.SerialNumber?.Trim();
            if (string.IsNullOrEmpty(number) && p.SerialId is int sid)
            {
                if (!numbersById.TryGetValue(sid, out var known) || known.ProductId != p.ProductId) throw new NotFoundException("Serie", null, true);
                number = known.Number;
            }
            if (string.IsNullOrEmpty(number)) continue;
            plans.Add(new SerialPlan { Index = i, Posting = p, Number = number });
        }
        if (plans.Count == 0) return plans;

        var dup = plans.GroupBy(s => (s.Posting.ProductId, s.Number.ToUpperInvariant())).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new ValidationException("serialNumbers", SerialRules.Duplicated(dup.First().Number));

        var statusById = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.SerialStatus)
            .ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct);
        foreach (var group in plans.GroupBy(s => s.Posting.ProductId).OrderBy(g => g.Key))
        {
            var locked = await db.LockSerialsAsync(group.Key, group.Select(s => s.Number).ToList(), ct);
            var map = locked.ToDictionary(s => s.SerialNumber, StringComparer.OrdinalIgnoreCase);
            foreach (var plan in group)
            {
                var p = plan.Posting;
                plan.Serial = map.GetValueOrDefault(plan.Number);
                var status = plan.Serial?.StatusCodeId is int st ? statusById.GetValueOrDefault(st) : null;
                if (p.SerialId is int given && plan.Serial is not null && plan.Serial.SerialId != given)
                    throw new ValidationException("serialNumbers", SerialNotOfProduct);
                if (p.FromWarehouseId is int fw)
                {
                    // Sale de (o se mueve desde) una posición y está en ella: AVAILABLE; con FromReserved, RESERVED (L328).
                    var where = p.FromBinId is int fb ? bins[fb].Code : skus.GetValueOrDefault(p.ProductId) ?? string.Empty;
                    var expected = p.FromReserved ? SerialStatuses.Reserved : SerialStatuses.Available;
                    if (plan.Serial is null || status != expected
                        || plan.Serial.CurrentWarehouseId != fw || plan.Serial.CurrentBinId != p.FromBinId)
                        throw new ConflictException(p.FromReserved ? SerialNotReserved(plan.Number, where) : SerialRules.NotAvailable(plan.Number, where));
                }
                else
                {
                    // Entra al inventario: una serie en inventario no se recibe de nuevo; una dada de baja no vuelve (D34).
                    if (plan.Serial is null) continue;
                    if (status == SerialStatuses.Scrapped) throw new ConflictException(SerialRules.Scrapped(plan.Number));
                    if (status is SerialStatuses.Available or SerialStatuses.Reserved || plan.Serial.CurrentWarehouseId is not null)
                        throw new ConflictException(SerialRules.AlreadyInStock(plan.Number));
                }
            }
        }
        return plans;
    }

    private async Task ApplySerialsAsync(List<SerialPlan> plans, CancellationToken ct)
    {
        if (plans.Count == 0) return;
        var born = new List<SerialPlan>();
        foreach (var plan in plans.Where(s => s.Serial is null))
        {
            var p = plan.Posting;
            plan.Serial = new InventorySerial { ProductId = p.ProductId, LotId = p.LotId, SerialNumber = plan.Number };
            db.InventorySerials.Add(plan.Serial);
            born.Add(plan);
        }
        if (born.Count > 0) await SaveAsync(ct);   // ids para el historial y el movimiento

        foreach (var plan in plans.OrderBy(s => s.Serial!.SerialId))
        {
            var p = plan.Posting;
            var serial = plan.Serial!;
            var sign = p.ToWarehouseId.HasValue ? 1 : -1;
            var target = serial.StatusCodeId is null && sign > 0 ? SerialStatuses.Available : SerialRules.TargetStatus(p.TxnType, sign);
            if (target is not null)
            {
                var current = serial.StatusCodeId is int sc
                    ? await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == sc).Select(s => s.InternalCode).FirstOrDefaultAsync(ct)
                    : null;
                if (current != target)
                {
                    var to = await statuses.TransitionAsync(StatusDomains.SerialStatus, EntityTypes.InventorySerial, serial.SerialId,
                        serial.StatusCodeId, target, null, ct);
                    serial.StatusCodeId = to.StatusCodeId;
                }
            }
            if (p.ToWarehouseId is int tw)
            {
                serial.CurrentWarehouseId = tw;
                serial.CurrentBinId = p.ToBinId;
                if (p.LotId.HasValue && serial.LotId is null) serial.LotId = p.LotId;
            }
            else
            {
                serial.CurrentWarehouseId = null;
                serial.CurrentBinId = null;
            }
        }
    }

    // ================================================================ apoyo

    private int RequireTenant() => tenant.TenantId ?? throw new ForbiddenException("No hay tenant activo en la sesión.");

    private static BalanceKey KeyOf(StockReservation r) => new(r.ProductId, r.WarehouseId, r.BinId, r.LotId);

    /// <summary>
    /// Cantidad válida y coherencia con la serie: un producto SERIAL reserva nombrando tantas series como la cantidad (entera);
    /// uno sin serie no las lleva (400 con Errors por reserva).
    /// </summary>
    private async Task ValidateReservationsAsync(IReadOnlyList<StockReservation> reservations, CancellationToken ct)
    {
        var productIds = reservations.Select(r => r.ProductId).Distinct().ToList();
        var trackingIds = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, p => p.TrackingTypeLookupId, ct);
        var serialTypeId = await lookups.TryGetIdAsync(LookupDomains.TrackingType, TrackingTypes.Serial, ct);
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < reservations.Count; i++)
        {
            var r = reservations[i];
            var serialCount = SerialsOf(r).Count;
            var isSerial = serialTypeId is int st && trackingIds.TryGetValue(r.ProductId, out var t) && t == st;
            var msg = InventoryRules.ValidateQuantity(r.Quantity)
                      ?? (isSerial
                          ? InventoryRules.ValidateTracking(TrackingTypes.Serial, r.LotId.HasValue, r.Quantity, serialCount)
                          : serialCount > 0 ? InventoryRules.SerialNotAllowed : null);
            if (msg is not null) errors[$"reservations[{i}]"] = new[] { msg };
        }
        var dup = reservations.SelectMany(r => SerialsOf(r).Select(n => (r.ProductId, Number: n)))
            .GroupBy(x => (x.ProductId, x.Number.ToUpperInvariant())).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) errors["serialNumbers"] = new[] { SerialRules.Duplicated(dup.First().Number) };
        if (errors.Count > 0) throw new ValidationException(errors);
    }

    private static List<string> SerialsOf(StockReservation r)
        => (r.SerialNumbers ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();

    /// <summary>
    /// Bloquea (por producto ascendente, SerialId ascendente) las series nombradas en las reservas y exige que estén en el
    /// estatus esperado y en la posición de la reserva (409 NotAvailable al reservar; SerialNotReserved al liberar).
    /// </summary>
    private async Task<List<InventorySerial>> LockReservationSerialsAsync(IReadOnlyList<StockReservation> reservations,
        IReadOnlyDictionary<int, string> binCodes, string expectedStatus, CancellationToken ct)
    {
        var result = new List<InventorySerial>();
        var withSerials = reservations.Where(r => SerialsOf(r).Count > 0).ToList();
        if (withSerials.Count == 0) return result;
        var expectedId = await db.StatusIdAsync(StatusDomains.SerialStatus, expectedStatus, ct);
        foreach (var group in withSerials.GroupBy(r => r.ProductId).OrderBy(g => g.Key))
        {
            var numbers = group.SelectMany(SerialsOf).ToList();
            var locked = await db.LockSerialsAsync(group.Key, numbers, ct);
            var map = locked.ToDictionary(s => s.SerialNumber, StringComparer.OrdinalIgnoreCase);
            foreach (var r in group)
                foreach (var number in SerialsOf(r))
                {
                    var where = binCodes.GetValueOrDefault(r.BinId) ?? string.Empty;
                    if (!map.TryGetValue(number, out var serial) || serial.StatusCodeId != expectedId
                        || serial.CurrentWarehouseId != r.WarehouseId || serial.CurrentBinId != r.BinId
                        || (r.LotId.HasValue && serial.LotId != r.LotId))
                        throw new ConflictException(expectedStatus == SerialStatuses.Reserved
                            ? SerialNotReserved(number, where)
                            : SerialRules.NotAvailable(number, where));
                    result.Add(serial);
                }
        }
        return result.OrderBy(s => s.SerialId).ToList();
    }

    private async Task TransitionSerialsAsync(List<InventorySerial> serials, string target, CancellationToken ct)
    {
        foreach (var serial in serials)
        {
            var to = await statuses.TransitionAsync(StatusDomains.SerialStatus, EntityTypes.InventorySerial, serial.SerialId,
                serial.StatusCodeId, target, null, ct);
            serial.StatusCodeId = to.StatusCodeId;
        }
    }

    private async Task<Dictionary<int, string>> SkusAsync(IEnumerable<int> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        return await db.Products.AsNoTracking().Where(p => ids.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, p => p.Sku, ct);
    }

    private async Task<Dictionary<int, string>> BinCodesAsync(IEnumerable<int?> binIds, CancellationToken ct)
    {
        var ids = binIds.Where(b => b.HasValue).Select(b => b!.Value).Distinct().ToList();
        return await db.WarehouseBins.AsNoTracking().Where(b => ids.Contains(b.WarehouseBinId)).ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code, ct);
    }

    /// <summary>SaveChanges con la última línea en SQL: CHECK (547) → 409 insufficient_stock; único (2601/2627) → 409.</summary>
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
        catch (DbUpdateException ex) when (IsCheckViolation(ex))
        {
            throw new InsufficientStockException("Inventario insuficiente: el saldo no puede quedar negativo ni con más reservado que en mano.");
        }
        catch (DbUpdateException ex) when (DbExtensions.IsUniqueViolation(ex))
        {
            throw new ConflictException("El movimiento de inventario chocó con otro simultáneo; intente de nuevo.");
        }
    }

    private static bool IsCheckViolation(DbUpdateException ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is SqlException sql) return sql.Number == 547 || sql.Errors.Cast<SqlError>().Any(err => err.Number == 547);
        return false;
    }
}
