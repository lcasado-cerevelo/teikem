using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 232 — KIT ARMADO: un producto (tipo ASSEMBLED_KIT) con EXISTENCIA PROPIA, formado por otros productos (su composición en ProductKitComponent). Se arma antes
/// (por el inventario) y luego se vende, reserva, recolecta, despacha y cuenta como cualquier producto: aquí no hay explosión en piezas ni sustitución.
/// - Alta (<see cref="CreateAsync"/>): POST /products con «es un kit armado», su composición y, opcional, cuántos armar. El rastreo del kit lo deciden las piezas:
///   LOT si alguna pieza va por lote, NONE si ninguna; piezas con serie NO se admiten en esta versión.
/// - Composición (<see cref="SetCompositionAsync"/>): se cambia solo mientras el kit no tenga armados, movimientos ni existencia (si no: 409, «cree un kit nuevo»).
/// - Armar (<see cref="AssembleAsync"/>): en UNA transacción y por el InventoryLedger (única vía de escritura), salen las piezas de N kits (motivo de sistema
///   KIT_ASSEMBLY, origen KIT_ASSEMBLY + id) de lo DISPONIBLE (no reservado; sin cuarentena, cross-dock ni en renta), por la rotación de siempre o del lote/posición
///   que se escoja, y entran los N kits a la posición destino. Nada queda reservado. Un solo lote por pieza en cada armado. Si el kit va por lote, cada armado crea
///   su lote (número = el del armado, vence con la pieza que vence primero). Costo del kit = valor de las piezas que salieron (ValuationService): el valor total del
///   inventario no cambia.
/// - Desarmar (<see cref="DisassembleAsync"/>): de UN armado, solo kits disponibles; salen los kits y cada pieza vuelve a su lote, a la posición indicada o a la de
///   donde salió, al costo con que salió (motivo KIT_DISASSEMBLY). Parcial permitido.
/// - Orden de bloqueo: encabezado del kit (Product, UPDLOCK) y después los saldos (los bloquea el ledger en orden de clave): dos armados del mismo kit se forman en fila
///   y dos armados que comparten piezas no dejan existencia negativa (el ledger revalida con el saldo bloqueado → 409 insufficient_stock).
/// </summary>
public sealed class AssembledKitService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, InventoryLedger ledger, InventoryReadService reads,
    ValuationService valuation, INumberSequenceService numbers, PermissionService permissions, ProductService products)
{
    public const string AssemblyLabel = "Armado";
    public const string NumberTaken = "Ese número de armado ya existe; intente de nuevo.";
    public const int MaxTake = 200;
    private static readonly int? CounterClientId = null;   // un contador por compañía

    private sealed record PieceInfo(int ProductId, Guid PublicId, string Sku, string Name, decimal Quantity, int SortOrder, bool IsActive, string Tracking, string? RotationMethod);

    // ================================================================ lectura

    /// <summary>GET /products/{id}/assembled-kit: composición, rastreo, si la composición ya no se cambia, «se pueden armar» por almacén y la existencia del kit.</summary>
    public async Task<AssembledKitDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var kit = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == publicId, ct) ?? throw WmsResolve.ProductNotFound();
        var tracking = (await lookups.GetAsync(kit.TrackingTypeLookupId, ct))?.InternalCode ?? TrackingTypes.None;
        if (!await IsAssembledKitAsync(kit, ct))
            return new AssembledKitDto(kit.PublicId, kit.Sku, kit.Name, false, tracking, false, Array.Empty<AssembledKitPieceDto>(), Array.Empty<AssembledKitAvailabilityDto>(), 0m, 0m);

        var pieces = await PiecesAsync(kit.ProductId, ct);
        var balances = await db.Set<StockBalance>().AsNoTracking().Where(b => b.ProductId == kit.ProductId).Select(b => new { b.QtyOnHand, b.QtyReserved }).ToListAsync(ct);
        var onHand = balances.Sum(b => b.QtyOnHand);
        var available = balances.Sum(b => b.QtyOnHand - b.QtyReserved);
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Code).ToListAsync(ct);
        var availability = new List<AssembledKitAvailabilityDto>();
        foreach (var w in warehouses)
        {
            var (one, total) = await CanAssembleAsync(w.WarehouseId, pieces, ct);
            availability.Add(new AssembledKitAvailabilityDto(w.PublicId, w.Code, one, total));
        }
        return new AssembledKitDto(kit.PublicId, kit.Sku, kit.Name, true, tracking, await IsLockedAsync(kit.ProductId, ct),
            pieces.Select(ToPieceDto).ToList(), availability, onHand, available);
    }

    /// <summary>GET /products/{id}/assembled-kit/assemblies: historial de armados (más reciente primero), cada uno con sus piezas y sus desarmados.</summary>
    public async Task<KitAssemblyPageDto> ListAssembliesAsync(Guid publicId, int skip, int take, CancellationToken ct)
    {
        var kit = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == publicId, ct) ?? throw WmsResolve.ProductNotFound();
        skip = Math.Max(0, skip);
        take = take <= 0 ? 50 : Math.Min(take, MaxTake);
        var query = db.Set<KitAssembly>().AsNoTracking().Where(a => a.KitProductId == kit.ProductId);
        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.KitAssemblyId).Skip(skip).Take(take).ToListAsync(ct);
        return new KitAssemblyPageDto(total, skip, take, await ToDtosAsync(page, ct));
    }

    // ================================================================ alta (POST /products con «es un kit armado»)

    /// <summary>
    /// Alta de un kit armado: valida la composición ANTES de crear nada; el rastreo lo deciden las piezas (un trackingType distinto → 400); en una transacción crea el
    /// producto (ProductService, mismas reglas del alta), lo marca como kit armado con su composición y, si se pidió, arma los kits (pide inventory.adjust; si el armado
    /// falla no queda nada).
    /// </summary>
    public async Task<ProductDetailDto> CreateAsync(ProductCreateRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        // Lote 233: solo el ALTA depende del ajuste de la compañía; los kits armados que ya existen siguen armándose, desarmándose y vendiéndose aunque se apague.
        if (!await db.Tenants.AsNoTracking().Where(t => t.TenantId == tenant.TenantId).Select(t => t.UsesAssembledKits).FirstOrDefaultAsync(ct))
            throw new StatusRuleException(AssembledKitRules.CompanyDoesNotUseAssembledKits);
        var assembling = req.AssembleQuantity is not null;
        if (assembling)
        {
            await permissions.EnsureAsync(PermissionCatalog.InventoryAdjust, ct);
            var errors = new Dictionary<string, string[]>();
            if (AssembledKitRules.ValidateKitQuantity(req.AssembleQuantity) is string qtyError) errors["assembleQuantity"] = new[] { qtyError };
            if (req.AssembleToBinId is null) errors["assembleToBinId"] = new[] { AssembledKitRules.AssembleOnCreateNeedsBin };
            if (errors.Count > 0) throw Fail(errors);
        }
        var assembledTypeId = await AssembledTypeIdAsync(ct);
        int? ownerId = req.OwnerClientPublicId is Guid ownerPublicId ? ClientQueries.EnsureClientActive(await db.ResolveClientAsync(ownerPublicId, ct)).ClientId : null;
        var (pieces, tracking) = await ValidatePiecesAsync(null, ownerId, req.KitPieces, "kitPieces", ct);
        if (!string.IsNullOrWhiteSpace(req.TrackingType) && !string.Equals(req.TrackingType.Trim(), tracking, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("trackingType", AssembledKitRules.TrackingDecidedByPieces(tracking));
        if (assembling) await numbers.EnsureAsync(AssembledKitCodes.AssemblyNumberKind, CounterClientId, ct);

        var productRequest = req with
        {
            TrackingType = tracking, IsAssembledKit = null, KitPieces = null, AssembleQuantity = null, AssembleWarehousePublicId = null, AssembleToBinId = null,
        };
        var publicId = await db.RunInTransactionAsync(async ct2 =>
        {
            var created = await products.CreateAsync(productRequest, ct2);
            var kit = await db.Set<Product>().AsTracking().FirstAsync(p => p.PublicId == created.Product.PublicId, ct2);
            kit.ProductTypeLookupId = assembledTypeId;
            var order = 0;
            foreach (var p in pieces)
                db.Set<ProductKitComponent>().Add(new ProductKitComponent { TenantId = kit.TenantId, KitProductId = kit.ProductId, ComponentProductId = p.ProductId, Quantity = p.Quantity, SortOrder = ++order });
            await db.SaveGuardedAsync(KitService.Concurrency, ct2);
            if (assembling)
                await AssembleCoreAsync(kit.ProductId, new KitAssembleRequest(req.AssembleQuantity, req.AssembleToBinId, req.AssembleWarehousePublicId), ct2);
            return kit.PublicId;
        }, ct);
        return await products.GetAsync(publicId, InventoryScope.Any, ct);
    }

    // ================================================================ composición

    /// <summary>PUT /products/{id}/assembled-kit: reemplaza la composición (y el rastreo que de ella resulta). Solo sin armados, movimientos ni existencia del kit.</summary>
    public async Task<AssembledKitDto> SetCompositionAsync(Guid publicId, AssembledKitSetRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var current = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == publicId, ct) ?? throw WmsResolve.ProductNotFound();
        if (!await IsAssembledKitAsync(current, ct)) throw new StatusRuleException(AssembledKitRules.NotAssembledKit(current.Sku));
        var (pieces, tracking) = await ValidatePiecesAsync(current.ProductId, current.ClientId, req.Pieces, "pieces", ct);
        var trackingId = await lookups.GetIdAsync(LookupDomains.TrackingType, tracking, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var kit = await db.LockProductAsync(current.ProductId, ct2);
            if (await IsLockedAsync(kit.ProductId, ct2)) throw new ConflictException(AssembledKitRules.CompositionLocked(kit.Sku));
            var existing = await db.Set<ProductKitComponent>().AsTracking().Where(c => c.KitProductId == kit.ProductId).ToListAsync(ct2);
            db.Set<ProductKitComponent>().RemoveRange(existing);
            var order = 0;
            foreach (var p in pieces)
                db.Set<ProductKitComponent>().Add(new ProductKitComponent { TenantId = kit.TenantId, KitProductId = kit.ProductId, ComponentProductId = p.ProductId, Quantity = p.Quantity, SortOrder = ++order });
            if (kit.TrackingTypeLookupId != trackingId)
            {
                ProductService.ApplyAssembledKitTracking(kit, trackingId);
                // la excepción de rotación debe aplicar al nuevo rastreo (con lote: FEFO/FIFO; sin lote: FIFO/por posición); si no, vuelve a la de la compañía
                if (kit.RotationMethod is not null && !RotationRules.AppliesTo(kit.RotationMethod, tracking == TrackingTypes.Lot)) kit.RotationMethod = null;
            }
            await db.SaveGuardedAsync(KitService.Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ================================================================ armar

    public async Task<KitAssemblyResultDto> AssembleAsync(Guid publicId, KitAssembleRequest req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.InventoryAdjust, ct);
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var errors = new Dictionary<string, string[]>();
        if (AssembledKitRules.ValidateKitQuantity(req.Quantity) is string qtyError) errors["quantity"] = new[] { qtyError };
        if (req.ToBinId is null) errors["toBinId"] = new[] { AssembledKitRules.ToBinRequired };
        if (AdjustmentRules.NormalizeNotes(req.Notes).Error is string notesError) errors["notes"] = new[] { notesError };
        if (errors.Count > 0) throw Fail(errors);
        var kitId = await db.Set<Product>().AsNoTracking().Where(p => p.PublicId == publicId).Select(p => (int?)p.ProductId).FirstOrDefaultAsync(ct) ?? throw WmsResolve.ProductNotFound();
        await numbers.EnsureAsync(AssembledKitCodes.AssemblyNumberKind, CounterClientId, ct);

        (int Id, List<long> Txns, List<BalanceKey> Keys) done = default;
        await db.RunInTransactionAsync(async ct2 => { done = await AssembleCoreAsync(kitId, req, ct2); }, ct);
        var assembly = (await ToDtosAsync(await db.Set<KitAssembly>().AsNoTracking().Where(a => a.KitAssemblyId == done.Id).ToListAsync(ct), ct))[0];
        return new KitAssemblyResultDto(assembly, new MovementResultDto(await reads.TransactionsByIdsAsync(done.Txns, ct), await reads.BalancesForKeysAsync(done.Keys, ct)));
    }

    /// <summary>El armado dentro de una transacción ya abierta (lo usan el armado suelto y el alta con «armar al crear»).</summary>
    private async Task<(int Id, List<long> Txns, List<BalanceKey> Keys)> AssembleCoreAsync(int kitProductId, KitAssembleRequest req, CancellationToken ct)
    {
        var kitCount = req.Quantity!.Value;
        var (notes, _) = AdjustmentRules.NormalizeNotes(req.Notes);

        // 1. Encabezado del kit bloqueado: dos armados del mismo kit (o un cambio de composición) se forman en fila.
        var kit = await db.LockProductAsync(kitProductId, ct);
        if (!await IsAssembledKitAsync(kit, ct)) throw new StatusRuleException(AssembledKitRules.NotAssembledKit(kit.Sku));
        if (!kit.IsActive) throw new StatusRuleException(InventoryRules.ProductInactiveMessage(kit.Sku));
        var kitTracking = (await lookups.GetAsync(kit.TrackingTypeLookupId, ct))?.InternalCode ?? TrackingTypes.None;
        var pieces = await PiecesAsync(kit.ProductId, ct);
        if (pieces.Count == 0) throw new StatusRuleException(AssembledKitRules.NoPieces(kit.Sku));
        foreach (var p in pieces.Where(p => p.Tracking == TrackingTypes.Serial)) throw new StatusRuleException(AssembledKitRules.PieceIsSerial(p.Sku));

        // 2. Almacén y posición destino (dentro de su almacén filtrado).
        var warehouse = await db.ResolveWarehouseOrDefaultAsync(req.WarehousePublicId, ct);
        if (!warehouse.IsActive) throw new StatusRuleException(InventoryRules.WarehouseInactiveMessage(warehouse.Code));
        var toBin = await ResolveUsableBinAsync(warehouse.WarehouseId, req.ToBinId!.Value, ct);

        // 3. Lo que se escogió por pieza (lote y/o posición).
        var choices = new Dictionary<int, AssemblyPieceChoice>();
        var choiceErrors = new Dictionary<string, string[]>();
        var list = req.Pieces ?? Array.Empty<AssemblyPieceChoice>();
        for (var i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (c?.ProductPublicId is not Guid pid) { choiceErrors[$"pieces[{i}].productPublicId"] = new[] { AssembledKitRules.PieceNotInKit }; continue; }
            var piece = pieces.FirstOrDefault(p => p.PublicId == pid);
            if (piece is null) { choiceErrors[$"pieces[{i}].productPublicId"] = new[] { AssembledKitRules.PieceNotInKit }; continue; }
            if (choices.ContainsKey(piece.ProductId)) { choiceErrors[$"pieces[{i}]"] = new[] { AssembledKitRules.PieceRepeated(piece.Sku) }; continue; }
            if (c.LotId is not null && piece.Tracking != TrackingTypes.Lot) { choiceErrors[$"pieces[{i}].lotId"] = new[] { AssembledKitRules.LotOnNonLotPiece(piece.Sku) }; continue; }
            choices[piece.ProductId] = c;
        }
        if (choiceErrors.Count > 0) throw Fail(choiceErrors);
        foreach (var (productId, c) in choices)
        {
            if (c.LotId is int lotId) await db.ResolveLotAsync(productId, lotId, ct);
            if (c.BinId is int binId) await db.ResolveBinAsync(warehouse.WarehouseId, binId, false, ct);
        }

        // 4. Candidatos disponibles de cada pieza en el almacén y reparto por la rotación (un solo lote por pieza).
        var pieceIds = pieces.Select(p => p.ProductId).ToList();
        var candidates = await CandidatesAsync(warehouse.WarehouseId, pieceIds, ct);
        var company = await RotationQueries.CompanyAsync(db, ct);
        var takes = new List<(PieceInfo Piece, KitPieceTake Take, DateOnly? Expiry)>();
        var shortages = new Dictionary<string, string[]>();
        string? firstShortage = null;
        var lotNumbers = await LotNumbersAsync(candidates.Values.SelectMany(c => c).Where(c => c.LotId.HasValue).Select(c => c.LotId!.Value)
            .Concat(choices.Values.Where(c => c.LotId.HasValue).Select(c => c.LotId!.Value)), ct);
        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            var need = piece.Quantity * kitCount;
            var lotTracked = piece.Tracking == TrackingTypes.Lot;
            var method = RotationRules.Resolve(piece.RotationMethod, company, lotTracked);
            choices.TryGetValue(piece.ProductId, out var choice);
            var eligible = PickBatchRules.Eligible(candidates.GetValueOrDefault(piece.ProductId) ?? new List<PickCandidate>(), choice?.BinId, choice?.LotId, method);
            string? shortage = null;
            IEnumerable<PickCandidate> source = eligible;
            if (lotTracked)
            {
                var lotId = choice?.LotId ?? eligible.FirstOrDefault()?.LotId;
                var ofLot = eligible.Where(c => c.LotId == lotId).ToList();
                var available = ofLot.Sum(c => c.Available);
                if (lotId is null || ofLot.Count == 0)
                    shortage = choice?.LotId is int chosen
                        ? AssembledKitRules.LotNotEnough(lotNumbers.GetValueOrDefault(chosen) ?? chosen.ToString(), piece.Sku, 0m, piece.Quantity)
                        : AssembledKitRules.PieceNotEnough(piece.Sku, warehouse.Code, 0m, need, (int)kitCount, piece.Quantity);
                else if (available < need)
                    shortage = AssembledKitRules.LotNotEnough(lotNumbers.GetValueOrDefault(lotId.Value) ?? lotId.Value.ToString(), piece.Sku, available, piece.Quantity);
                source = ofLot;
            }
            else
            {
                var available = eligible.Sum(c => c.Available);
                if (available < need) shortage = AssembledKitRules.PieceNotEnough(piece.Sku, warehouse.Code, available, need, (int)kitCount, piece.Quantity);
            }
            if (shortage is not null)
            {
                shortages[$"pieces[{i}]"] = new[] { shortage };
                firstShortage ??= shortage;
                continue;
            }
            var left = need;
            foreach (var c in source)
            {
                if (left <= 0m) break;
                var q = Math.Min(left, c.Available);
                takes.Add((piece, new KitPieceTake(c.BinId, c.LotId, q), c.ExpiryDate));
                left -= q;
            }
        }
        if (shortages.Count > 0) throw new InsufficientStockException(firstShortage!, shortages);

        // 5. Documento (y el lote del kit, con el mismo número) ANTES de los movimientos: el origen va en el INSERT del ledger.
        var seq = await numbers.NextAsync(AssembledKitCodes.AssemblyNumberKind, CounterClientId, ct);
        var number = NumberFormat.Resolve(AssembledKitCodes.AssemblyPattern, seq);
        InventoryLot? kitLot = null;
        if (kitTracking == TrackingTypes.Lot)
        {
            kitLot = new InventoryLot { ProductId = kit.ProductId, LotNumber = number, ExpiryDate = AssembledKitRules.KitExpiry(takes.Select(t => t.Expiry)), IsActive = true };
            db.Set<InventoryLot>().Add(kitLot);
            await db.SaveGuardedAsync(NumberTaken, ct);
        }
        var doc = new KitAssembly
        {
            PublicId = Guid.NewGuid(), TenantId = kit.TenantId, Number = number, KitProductId = kit.ProductId, WarehouseId = warehouse.WarehouseId, ToBinId = toBin.WarehouseBinId,
            KitLotId = kitLot?.LotId, Quantity = kitCount, Notes = notes, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
        };
        foreach (var (piece, take, _) in takes)
            doc.Lines.Add(new KitAssemblyLine { TenantId = kit.TenantId, ComponentProductId = piece.ProductId, LotId = take.LotId, FromBinId = take.BinId, QtyPerKit = piece.Quantity, Quantity = take.Quantity });
        db.Add(doc);
        await db.SaveGuardedAsync(NumberTaken, ct);

        // 6. Movimientos: salen las piezas (cada toma) y entran los kits. Nada se reserva.
        var note = KitNote("Armado de kit", kit.Sku, number, notes);
        var lines = doc.Lines.ToList();
        var postings = lines.Select(l => new InventoryPosting(InventoryTxnTypes.Adjustment, l.ComponentProductId, l.Quantity, LotId: l.LotId,
            FromWarehouseId: warehouse.WarehouseId, FromBinId: l.FromBinId, RefEntityType: AssembledKitCodes.AssemblyEntityType, RefId: doc.KitAssemblyId,
            ReasonCode: AdjustmentReasons.KitAssembly, Notes: note)).ToList();
        postings.Add(new InventoryPosting(InventoryTxnTypes.Adjustment, kit.ProductId, kitCount, LotId: kitLot?.LotId, ToWarehouseId: warehouse.WarehouseId,
            ToBinId: toBin.WarehouseBinId, RefEntityType: AssembledKitCodes.AssemblyEntityType, RefId: doc.KitAssemblyId, ReasonCode: AdjustmentReasons.KitAssembly, Notes: note));
        var txnIds = (await ledger.PostAsync(postings, ct)).ToList();
        for (var i = 0; i < lines.Count; i++) lines[i].InventoryTransactionId = txnIds[i];

        // 7. Costo: el valor de lo que salió de cada pieza (con el inventario ya asentado) es el costo del kit; el valor total no cambia.
        var issued = lines.GroupBy(l => l.ComponentProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var values = await valuation.ConsumedValueAsync(issued, ct);
        decimal? total = null;
        foreach (var (productId, qty) in issued)
        {
            var (value, unvalued) = values.GetValueOrDefault(productId);
            decimal? unit = unvalued >= qty ? null : InventoryRules.Round4(value / qty);
            foreach (var l in lines.Where(l => l.ComponentProductId == productId)) l.UnitCost = unit;
            if (unit is not null) total = (total ?? 0m) + value;
        }
        doc.TotalCost = total is decimal t ? InventoryRules.Round4(t) : null;
        doc.UnitCost = total is decimal t2 ? InventoryRules.Round4(t2 / kitCount) : null;
        await db.SaveGuardedAsync(NumberTaken, ct);

        var keys = postings.Select(p => p.FromWarehouseId is int fw ? new BalanceKey(p.ProductId, fw, p.FromBinId, p.LotId) : new BalanceKey(p.ProductId, p.ToWarehouseId!.Value, p.ToBinId, p.LotId))
            .Distinct().ToList();
        return (doc.KitAssemblyId, txnIds, keys);
    }

    // ================================================================ desarmar

    public async Task<KitDisassemblyResultDto> DisassembleAsync(Guid publicId, Guid assemblyPublicId, KitDisassembleRequest req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.InventoryAdjust, ct);
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var errors = new Dictionary<string, string[]>();
        if (AssembledKitRules.ValidateKitQuantity(req.Quantity) is string qtyError) errors["quantity"] = new[] { qtyError };
        var (notes, notesError) = AdjustmentRules.NormalizeNotes(req.Notes);
        if (notesError is not null) errors["notes"] = new[] { notesError };
        if (errors.Count > 0) throw Fail(errors);
        var kitId = await db.Set<Product>().AsNoTracking().Where(p => p.PublicId == publicId).Select(p => (int?)p.ProductId).FirstOrDefaultAsync(ct) ?? throw WmsResolve.ProductNotFound();
        await numbers.EnsureAsync(AssembledKitCodes.DisassemblyNumberKind, CounterClientId, ct);
        var count = req.Quantity!.Value;

        (int AssemblyId, Guid DisassemblyPublicId, List<long> Txns, List<BalanceKey> Keys) done = default;
        await db.RunInTransactionAsync(async ct2 =>
        {
            var kit = await db.LockProductAsync(kitId, ct2);
            if (!await IsAssembledKitAsync(kit, ct2)) throw new StatusRuleException(AssembledKitRules.NotAssembledKit(kit.Sku));
            var assembly = await db.Set<KitAssembly>().AsTracking().FirstOrDefaultAsync(a => a.PublicId == assemblyPublicId && a.KitProductId == kit.ProductId, ct2)
                           ?? throw new NotFoundException(AssemblyLabel);
            var remaining = assembly.Quantity - assembly.DisassembledQty;
            if (count > remaining) throw new ConflictException(AssembledKitRules.AssemblyRemaining(assembly.Number, remaining, count));
            var assemblyLines = await db.Set<KitAssemblyLine>().AsTracking().Where(l => l.KitAssemblyId == assembly.KitAssemblyId).OrderBy(l => l.KitAssemblyLineId).ToListAsync(ct2);

            var warehouse = req.WarehousePublicId is Guid wp
                ? await db.ResolveWarehouseAsync(wp, false, ct2)
                : await db.Set<Warehouse>().AsNoTracking().FirstAsync(w => w.WarehouseId == assembly.WarehouseId, ct2);
            if (req.FromBinId is int fromBin) await db.ResolveBinAsync(warehouse.WarehouseId, fromBin, false, ct2);
            WarehouseBin? toBin = req.ToBinId is int tb ? await ResolveUsableBinAsync(warehouse.WarehouseId, tb, ct2) : null;
            if (toBin is null && warehouse.WarehouseId != assembly.WarehouseId) throw new ValidationException("toBinId", AssembledKitRules.PiecesBinRequired);

            // Los kits salen de lo DISPONIBLE de ese armado (su lote; sin lote, los kits sin lote) en el almacén: lo reservado por una orden no se desarma.
            var kitCandidates = (await CandidatesAsync(warehouse.WarehouseId, new[] { kit.ProductId }, ct2)).GetValueOrDefault(kit.ProductId) ?? new List<PickCandidate>();
            var eligible = PickBatchRules.Eligible(kitCandidates.Where(c => c.LotId == assembly.KitLotId), req.FromBinId, null).ToList();
            var available = eligible.Sum(c => c.Available);
            if (available < count) throw new InsufficientStockException(AssembledKitRules.KitsNotAvailable(assembly.Number, warehouse.Code, available, count));

            var seq = await numbers.NextAsync(AssembledKitCodes.DisassemblyNumberKind, CounterClientId, ct2);
            var number = NumberFormat.Resolve(AssembledKitCodes.DisassemblyPattern, seq);
            var doc = new KitDisassembly
            {
                PublicId = Guid.NewGuid(), TenantId = kit.TenantId, Number = number, KitAssemblyId = assembly.KitAssemblyId, KitProductId = kit.ProductId,
                WarehouseId = warehouse.WarehouseId, KitLotId = assembly.KitLotId, Quantity = count, Notes = notes, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
            };
            // Cada pieza vuelve a su mismo lote: se reparte entre las líneas del armado (en su orden) sin pasar lo que cada una sacó.
            foreach (var group in assemblyLines.GroupBy(l => l.ComponentProductId))
            {
                var groupLines = group.ToList();
                var back = groupLines[0].QtyPerKit * count;
                foreach (var (index, qty) in AssembledKitRules.SplitReturn(back, groupLines.Select(l => (l.Quantity, l.ReturnedQty)).ToList()))
                {
                    var line = groupLines[index];
                    line.ReturnedQty += qty;
                    doc.Lines.Add(new KitDisassemblyLine
                    {
                        TenantId = kit.TenantId, KitAssemblyLineId = line.KitAssemblyLineId, ComponentProductId = line.ComponentProductId, LotId = line.LotId,
                        ToBinId = toBin?.WarehouseBinId ?? line.FromBinId, Quantity = qty, UnitCost = line.UnitCost,
                    });
                }
            }
            assembly.DisassembledQty += count;
            db.Add(doc);
            await db.SaveGuardedAsync(NumberTaken, ct2);

            var note = KitNote("Desarmado de kit", kit.Sku, $"{number} · {assembly.Number}", notes);
            var postings = new List<InventoryPosting>();
            var left = count;
            foreach (var c in eligible)
            {
                if (left <= 0m) break;
                var q = Math.Min(left, c.Available);
                postings.Add(new InventoryPosting(InventoryTxnTypes.Adjustment, kit.ProductId, q, LotId: assembly.KitLotId, FromWarehouseId: warehouse.WarehouseId,
                    FromBinId: c.BinId, RefEntityType: AssembledKitCodes.DisassemblyEntityType, RefId: doc.KitDisassemblyId, ReasonCode: AdjustmentReasons.KitDisassembly, Notes: note));
                left -= q;
            }
            var kitPostings = postings.Count;
            var docLines = doc.Lines.ToList();
            postings.AddRange(docLines.Select(l => new InventoryPosting(InventoryTxnTypes.Adjustment, l.ComponentProductId, l.Quantity, LotId: l.LotId,
                ToWarehouseId: warehouse.WarehouseId, ToBinId: l.ToBinId, RefEntityType: AssembledKitCodes.DisassemblyEntityType, RefId: doc.KitDisassemblyId,
                ReasonCode: AdjustmentReasons.KitDisassembly, Notes: note)));
            var txnIds = (await ledger.PostAsync(postings, ct2)).ToList();
            for (var i = 0; i < docLines.Count; i++) docLines[i].InventoryTransactionId = txnIds[kitPostings + i];
            await db.SaveGuardedAsync(NumberTaken, ct2);
            var keys = postings.Select(p => p.FromWarehouseId is int fw ? new BalanceKey(p.ProductId, fw, p.FromBinId, p.LotId) : new BalanceKey(p.ProductId, p.ToWarehouseId!.Value, p.ToBinId, p.LotId))
                .Distinct().ToList();
            done = (assembly.KitAssemblyId, doc.PublicId, txnIds, keys);
        }, ct);

        var dto = (await ToDtosAsync(await db.Set<KitAssembly>().AsNoTracking().Where(a => a.KitAssemblyId == done.AssemblyId).ToListAsync(ct), ct))[0];
        var dis = dto.Disassemblies.First(d => d.PublicId == done.DisassemblyPublicId);
        return new KitDisassemblyResultDto(dto, dis, new MovementResultDto(await reads.TransactionsByIdsAsync(done.Txns, ct), await reads.BalancesForKeysAsync(done.Keys, ct)));
    }

    // ================================================================ apoyo

    private async Task<int> AssembledTypeIdAsync(CancellationToken ct)
        => await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.AssembledKit, ct)
           ?? throw new InvalidOperationException("Falta el catálogo de tipos de producto (ProductType / ASSEMBLED_KIT).");

    private async Task<bool> IsAssembledKitAsync(Product product, CancellationToken ct)
        => product.ProductTypeLookupId is int t && t == await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.AssembledKit, ct);

    /// <summary>¿La composición ya no se puede cambiar? Con algún armado, algún movimiento del kit o existencia/reserva.</summary>
    private async Task<bool> IsLockedAsync(int kitProductId, CancellationToken ct)
        => await db.Set<KitAssembly>().AsNoTracking().AnyAsync(a => a.KitProductId == kitProductId, ct)
           || await db.Set<InventoryTransaction>().AsNoTracking().AnyAsync(t => t.ProductId == kitProductId, ct)
           || await db.Set<StockBalance>().AsNoTracking().AnyAsync(b => b.ProductId == kitProductId && (b.QtyOnHand != 0m || b.QtyReserved != 0m), ct);

    private async Task<List<PieceInfo>> PiecesAsync(int kitProductId, CancellationToken ct)
    {
        var rows = await (from c in db.Set<ProductKitComponent>().AsNoTracking()
                          join p in db.Set<Product>().AsNoTracking() on c.ComponentProductId equals p.ProductId
                          where c.KitProductId == kitProductId
                          orderby c.SortOrder, c.ProductKitComponentId
                          select new { p.ProductId, p.PublicId, p.Sku, p.Name, c.Quantity, c.SortOrder, p.IsActive, p.TrackingTypeLookupId, p.RotationMethod }).ToListAsync(ct);
        var result = new List<PieceInfo>(rows.Count);
        foreach (var r in rows)
            result.Add(new PieceInfo(r.ProductId, r.PublicId, r.Sku, r.Name, r.Quantity, r.SortOrder, r.IsActive,
                (await lookups.GetAsync(r.TrackingTypeLookupId, ct))?.InternalCode ?? TrackingTypes.None, r.RotationMethod));
        return result;
    }

    private static AssembledKitPieceDto ToPieceDto(PieceInfo p) => new(p.PublicId, p.Sku, p.Name, p.Quantity, p.SortOrder, p.IsActive, p.Tracking);

    /// <summary>La composición de un kit armado para la ficha del producto (ProductService la usa en el detalle).</summary>
    internal static async Task<IReadOnlyList<AssembledKitPieceDto>> PiecesForDetailAsync(TeikemDbContext db, ILookupCache lookups, int kitProductId, CancellationToken ct)
    {
        var rows = await (from c in db.Set<ProductKitComponent>().AsNoTracking()
                          join p in db.Set<Product>().AsNoTracking() on c.ComponentProductId equals p.ProductId
                          where c.KitProductId == kitProductId
                          orderby c.SortOrder, c.ProductKitComponentId
                          select new { p.PublicId, p.Sku, p.Name, c.Quantity, c.SortOrder, p.IsActive, p.TrackingTypeLookupId }).ToListAsync(ct);
        var result = new List<AssembledKitPieceDto>(rows.Count);
        foreach (var r in rows)
            result.Add(new AssembledKitPieceDto(r.PublicId, r.Sku, r.Name, r.Quantity, r.SortOrder, r.IsActive, (await lookups.GetAsync(r.TrackingTypeLookupId, ct))?.InternalCode ?? TrackingTypes.None));
        return result;
    }

    private sealed record ValidPiece(int ProductId, decimal Quantity, string Tracking);

    /// <summary>
    /// Composición válida: al menos una pieza y como máximo 50; cada una con producto y cantidad &gt; 0 (3 decimales); productos de la compañía (404 'Componente no
    /// encontrado.'), sin repetir, no el propio kit, no kits (ni de oferta ni armados), activos, del mismo dueño de inventario y sin serie. Devuelve las piezas y el rastreo
    /// que resulta (LOT si alguna va por lote).
    /// </summary>
    private async Task<(List<ValidPiece> Pieces, string Tracking)> ValidatePiecesAsync(int? kitProductId, int? ownerId, IReadOnlyList<AssembledKitPieceInput>? input, string field, CancellationToken ct)
    {
        if (input is null || input.Count == 0) throw new ValidationException(field, KitRules.NeedComponents);
        if (input.Count > KitCodes.MaxComponents) throw new ValidationException(field, KitRules.TooManyComponents());
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < input.Count; i++)
        {
            if (input[i]?.ProductPublicId is null) errors[$"{field}[{i}].productPublicId"] = new[] { "Indique el producto del componente." };
            else if (input[i].Quantity is not decimal q || q <= 0m) errors[$"{field}[{i}].quantity"] = new[] { KitRules.ComponentQuantity };
            else if (InventoryRules.ValidateQuantity(q) is string qe) errors[$"{field}[{i}].quantity"] = new[] { qe };
        }
        if (errors.Count > 0) throw Fail(errors);

        var publicIds = input.Select(c => c.ProductPublicId!.Value).ToList();
        var found = await db.Set<Product>().AsNoTracking().Where(p => publicIds.Contains(p.PublicId)).ToDictionaryAsync(p => p.PublicId, ct);
        var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
        var assembledTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.AssembledKit, ct);
        var seen = new HashSet<Guid>();
        var result = new List<ValidPiece>();
        for (var i = 0; i < input.Count; i++)
        {
            var id = input[i].ProductPublicId!.Value;
            if (!found.TryGetValue(id, out var p)) throw new NotFoundException(KitRules.ComponentNotFound);
            var tracking = (await lookups.GetAsync(p.TrackingTypeLookupId, ct))?.InternalCode ?? TrackingTypes.None;
            var key = $"{field}[{i}]";
            if (!seen.Add(id)) errors[key] = new[] { KitRules.DuplicateComponent(p.Sku) };
            else if (kitProductId is int k && p.ProductId == k) errors[key] = new[] { KitRules.SelfComponent(p.Sku) };
            else if (p.ProductTypeLookupId is int t && (t == kitTypeId || t == assembledTypeId)) errors[key] = new[] { AssembledKitRules.PieceIsKit(p.Sku) };
            else if (!p.IsActive) errors[key] = new[] { KitRules.ComponentInactive(p.Sku) };
            else if (p.ClientId != ownerId) errors[key] = new[] { KitRules.ComponentOtherOwner(p.Sku) };
            else if (tracking == TrackingTypes.Serial) errors[key] = new[] { AssembledKitRules.PieceIsSerial(p.Sku) };
            else result.Add(new ValidPiece(p.ProductId, input[i].Quantity!.Value, tracking));
        }
        if (errors.Count > 0) throw Fail(errors);
        return (result, AssembledKitRules.DecideTracking(result.Select(r => r.Tracking)));
    }

    private static ValidationException Fail(Dictionary<string, string[]> errors)
        => errors.Count == 1 ? new ValidationException(errors.Keys.First(), errors.Values.First()[0]) : new ValidationException(errors);

    /// <summary>Posición del almacén que admite kits: existe (404) y no es de cuarentena, en renta ni cross-dock (422). Si está inactiva lo dice el ledger (422).</summary>
    private async Task<WarehouseBin> ResolveUsableBinAsync(int warehouseId, int binId, CancellationToken ct)
    {
        var row = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                         join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                         where b.WarehouseId == warehouseId && b.WarehouseBinId == binId
                         select new { Bin = b, z.ZoneTypeLookupId }).FirstOrDefaultAsync(ct)
                  ?? throw WmsResolve.BinNotFound();
        var zoneType = row.ZoneTypeLookupId is int zt ? (await lookups.GetAsync(zt, ct))?.InternalCode : null;
        if (StockAllocator.IsExcludedZone(zoneType)) throw new StatusRuleException(AssembledKitRules.BinNotAllowed(row.Bin.Code));
        return row.Bin;
    }

    /// <summary>Saldos con disponible (en mano − reservado &gt; 0) de los productos en el almacén, en posiciones activas, con su zona, lote y vencimiento.</summary>
    private async Task<Dictionary<int, List<PickCandidate>>> CandidatesAsync(int warehouseId, IReadOnlyCollection<int> productIds, CancellationToken ct)
    {
        var rows = await (from b in db.Set<StockBalance>().AsNoTracking()
                          join bin in db.Set<WarehouseBin>().AsNoTracking() on b.WarehouseBinId equals (int?)bin.WarehouseBinId
                          join z in db.Set<WarehouseZone>().AsNoTracking() on bin.WarehouseZoneId equals z.WarehouseZoneId
                          join l in db.Set<InventoryLot>().AsNoTracking() on b.LotId equals (int?)l.LotId into lots
                          from lot in lots.DefaultIfEmpty()
                          where b.WarehouseId == warehouseId && bin.WarehouseId == warehouseId && productIds.Contains(b.ProductId) && b.QtyOnHand - b.QtyReserved > 0m
                          select new
                          {
                              b.ProductId, bin.WarehouseBinId, BinCode = bin.Code, bin.IsActive, z.ZoneTypeLookupId, b.LotId,
                              Expiry = lot != null ? lot.ExpiryDate : null, Available = b.QtyOnHand - b.QtyReserved, b.FirstReceivedAtUtc,
                          }).ToListAsync(ct);
        var zoneTypes = new Dictionary<int, string?>();
        foreach (var id in rows.Where(r => r.ZoneTypeLookupId != null).Select(r => r.ZoneTypeLookupId!.Value).Distinct())
            zoneTypes[id] = (await lookups.GetAsync(id, ct))?.InternalCode;
        return rows.GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => g.Select(r => new PickCandidate(r.WarehouseBinId, r.BinCode,
            r.ZoneTypeLookupId is int zt ? zoneTypes.GetValueOrDefault(zt) : null, r.IsActive, r.LotId, r.Expiry, r.Available, r.FirstReceivedAtUtc)).ToList());
    }

    /// <summary>«Se pueden armar» en un almacén: en un solo armado (un lote por pieza) y en total (todos los lotes).</summary>
    private async Task<(int One, int Total)> CanAssembleAsync(int warehouseId, IReadOnlyList<PieceInfo> pieces, CancellationToken ct)
    {
        if (pieces.Count == 0 || pieces.Any(p => p.Tracking == TrackingTypes.Serial)) return (0, 0);
        var candidates = await CandidatesAsync(warehouseId, pieces.Select(p => p.ProductId).ToList(), ct);
        var summary = pieces.Select(p =>
        {
            var eligible = PickBatchRules.Eligible(candidates.GetValueOrDefault(p.ProductId) ?? new List<PickCandidate>());
            var byLot = eligible.Where(c => c.LotId.HasValue).GroupBy(c => c.LotId!.Value).ToDictionary(g => g.Key, g => g.Sum(c => c.Available));
            return (p.Quantity, p.Tracking == TrackingTypes.Lot, (IReadOnlyDictionary<int, decimal>)byLot, eligible.Sum(c => c.Available));
        }).ToList();
        var one = AssembledKitRules.CanAssembleInOne(summary);
        var total = KitRules.CanBuild(summary.Select(s => (s.Item4, s.Quantity)));
        return (one, total);
    }

    private async Task<Dictionary<int, string>> LotNumbersAsync(IEnumerable<int> lotIds, CancellationToken ct)
    {
        var ids = lotIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();
        return await db.Set<InventoryLot>().AsNoTracking().Where(l => ids.Contains(l.LotId)).ToDictionaryAsync(l => l.LotId, l => l.LotNumber, ct);
    }

    /// <summary>Nota del movimiento: «Armado de kit {SKU} · ARM-00001» (y la nota del usuario, si la hay), dentro de las 300 posiciones del Kárdex.</summary>
    private static string KitNote(string what, string sku, string number, string? notes)
    {
        var text = $"{what} {sku} · {number}" + (string.IsNullOrWhiteSpace(notes) ? string.Empty : $" · {notes}");
        return text.Length <= AdjustmentRules.MaxNotesLength ? text : text[..AdjustmentRules.MaxNotesLength];
    }

    private async Task<IReadOnlyList<KitAssemblyDto>> ToDtosAsync(IReadOnlyList<KitAssembly> docs, CancellationToken ct)
    {
        if (docs.Count == 0) return Array.Empty<KitAssemblyDto>();
        var showCost = await permissions.HasPermissionAsync(PermissionCatalog.InventoryValue, ct);
        var ids = docs.Select(d => d.KitAssemblyId).ToList();
        var lines = await db.Set<KitAssemblyLine>().AsNoTracking().Where(l => ids.Contains(l.KitAssemblyId)).OrderBy(l => l.KitAssemblyLineId).ToListAsync(ct);
        var disassemblies = await db.Set<KitDisassembly>().AsNoTracking().Where(d => ids.Contains(d.KitAssemblyId)).OrderBy(d => d.CreatedAtUtc).ThenBy(d => d.KitDisassemblyId).ToListAsync(ct);
        var disIds = disassemblies.Select(d => d.KitDisassemblyId).ToList();
        var disLines = disIds.Count == 0 ? new List<KitDisassemblyLine>()
            : await db.Set<KitDisassemblyLine>().AsNoTracking().Where(l => disIds.Contains(l.KitDisassemblyId)).OrderBy(l => l.KitDisassemblyLineId).ToListAsync(ct);

        var productIds = docs.Select(d => d.KitProductId).Concat(lines.Select(l => l.ComponentProductId)).Concat(disLines.Select(l => l.ComponentProductId)).Distinct().ToList();
        var productsById = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).Select(p => new { p.ProductId, p.PublicId, p.Sku, p.Name })
            .ToDictionaryAsync(p => p.ProductId, ct);
        var lotIds = docs.Where(d => d.KitLotId.HasValue).Select(d => d.KitLotId!.Value).Concat(lines.Where(l => l.LotId.HasValue).Select(l => l.LotId!.Value))
            .Concat(disLines.Where(l => l.LotId.HasValue).Select(l => l.LotId!.Value)).Distinct().ToList();
        var lots = lotIds.Count == 0 ? new Dictionary<int, InventoryLot>()
            : await db.Set<InventoryLot>().AsNoTracking().Where(l => lotIds.Contains(l.LotId)).ToDictionaryAsync(l => l.LotId, ct);
        var binIds = docs.Select(d => d.ToBinId).Concat(lines.Select(l => l.FromBinId)).Concat(disLines.Select(l => l.ToBinId)).Distinct().ToList();
        var bins = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                          join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                          where binIds.Contains(b.WarehouseBinId)
                          select new { b.WarehouseBinId, b.Code }).ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code, ct);
        var whIds = docs.Select(d => d.WarehouseId).Concat(disassemblies.Select(d => d.WarehouseId)).Distinct().ToList();
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, w => new { w.PublicId, w.Code }, ct);
        var userIds = docs.Where(d => d.CreatedBy.HasValue).Select(d => d.CreatedBy!.Value).Concat(disassemblies.Where(d => d.CreatedBy.HasValue).Select(d => d.CreatedBy!.Value)).Distinct().ToList();
        var users = userIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, Name = u.FullName ?? u.Email ?? u.UserName ?? "" })
                .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        decimal? Cost(decimal? value) => showCost ? value : null;
        string? LotNo(int? id) => id is int l && lots.TryGetValue(l, out var lot) ? lot.LotNumber : null;

        return docs.Select(d =>
        {
            var kitP = productsById[d.KitProductId];
            var dLines = lines.Where(l => l.KitAssemblyId == d.KitAssemblyId).Select(l =>
            {
                var p = productsById[l.ComponentProductId];
                return new KitAssemblyLineDto(p.PublicId, p.Sku, p.Name, l.LotId, LotNo(l.LotId), l.LotId is int li && lots.TryGetValue(li, out var lot) ? lot.ExpiryDate : null,
                    l.FromBinId, bins.GetValueOrDefault(l.FromBinId) ?? string.Empty, l.QtyPerKit, l.Quantity, l.ReturnedQty, Cost(l.UnitCost));
            }).ToList();
            var dDis = disassemblies.Where(x => x.KitAssemblyId == d.KitAssemblyId).Select(x => new KitDisassemblyDto(x.PublicId, x.Number, x.Quantity,
                warehouses[x.WarehouseId].PublicId, warehouses[x.WarehouseId].Code, x.Notes, x.CreatedAtUtc, x.CreatedBy, x.CreatedBy is int u ? users.GetValueOrDefault(u) : null,
                disLines.Where(l => l.KitDisassemblyId == x.KitDisassemblyId).Select(l =>
                {
                    var p = productsById[l.ComponentProductId];
                    return new KitDisassemblyLineDto(p.PublicId, p.Sku, p.Name, l.LotId, LotNo(l.LotId), l.ToBinId, bins.GetValueOrDefault(l.ToBinId) ?? string.Empty, l.Quantity, Cost(l.UnitCost));
                }).ToList())).ToList();
            return new KitAssemblyDto(d.PublicId, d.Number, kitP.PublicId, kitP.Sku, d.Quantity, d.DisassembledQty, d.Quantity - d.DisassembledQty,
                warehouses[d.WarehouseId].PublicId, warehouses[d.WarehouseId].Code, d.ToBinId, bins.GetValueOrDefault(d.ToBinId) ?? string.Empty,
                d.KitLotId, LotNo(d.KitLotId), d.KitLotId is int kl && lots.TryGetValue(kl, out var klot) ? klot.ExpiryDate : null,
                Cost(d.UnitCost), Cost(d.TotalCost), d.Notes, d.CreatedAtUtc, d.CreatedBy, d.CreatedBy is int cu ? users.GetValueOrDefault(cu) : null, dLines, dDis);
        }).ToList();
    }
}
