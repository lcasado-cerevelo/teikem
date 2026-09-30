using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 14 (P1) — conciliación Kárdex ↔ saldo con tabla de descuadres (D5) y su resolución. Síncrona: la revisión automática
/// en segundo plano (P2, D14) se engancha a CheckProductsAsync; el barrido programado (fuera del lote) a SweepAsync.
///
/// Revisar (CheckProductsAsync, SweepAsync, RunAsync = "Ejecutar conciliación"):
/// 1. Primera pasada SIN bloqueos, agregada en SQL (InventoryReconciler), sobre los productos pedidos o todo el tenant.
/// 2. Solo los productos descuadrados y los que tienen un descuadre abierto se CONFIRMAN bajo bloqueo, uno por uno en
///    ProductId ascendente y cada uno en su transacción: rango de saldos del producto con UPDLOCK + HOLDLOCK
///    (LockBalancesByProductAsync, que respeta el orden de bloqueo del ledger) y recálculo. Así un movimiento a medias entre dos
///    consultas no produce un falso descuadre.
/// 3. ReconciliationRules.Plan: nuevo → OPEN (TransitionAsync null → OPEN); abierto → se actualizan cifras, revisiones y
///    LastCheckedAtUtc; abierto que cuadra → SELF_CORRECTED; descartado con las mismas cifras → no se reabre.
/// Orden de bloqueo: saldos del producto → descuadres. Resolver sigue el mismo orden (el saldo lo bloquea el ledger y el
/// descuadre se escribe al final, con su RowVersion como control de concurrencia): no hay ciclo.
///
/// Resolver (ResolveAsync, inventory.adjust): REBUILD_BALANCE (InventoryLedger.RebuildBalanceAsync: el saldo toma lo que da el
/// Kárdex; si ya cuadraba el descuadre se cierra solo) o DISMISS con nota. Ambos sellan ClosedAtUtc, ResolvedBy y la nota y
/// pasan por StatusService.TransitionAsync (historial). Sin IStatusTransitionEffect: el sello se escribe aquí, en la misma
/// unidad de trabajo.
/// El TenantId sale del principal: InventoryDiscrepancy es ITenantScoped (filtro global); se expone por PublicId.
/// </summary>
public sealed class InventoryReconciliationService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    InventoryReconciler reconciler,
    InventoryLedger ledger,
    InventoryReadService reads,
    ITenantClock clock,
    InventoryReconciliationQueue? queue = null)
{
    public const string ConcurrentCheckMessage = "La conciliación chocó con otra revisión simultánea; intente de nuevo.";
    public static string UnknownKind(string code) => $"Tipo de descuadre desconocido: '{code}'.";
    /// <summary>Movimientos recientes de la ficha.</summary>
    public const int RecentMovementsCount = 20;

    private sealed record ProductOutcome(int Opened, int StillOpen, int SelfCorrected, IReadOnlyList<ReconciliationMismatch> Mismatches);

    // ================================================================ revisar

    /// <summary>
    /// "Ejecutar conciliación" (MANUAL): con productos (≤ 200; más → 400; uno que no existe → 404 'Producto no encontrado.')
    /// revisa esos; sin productos, todo el tenant. Guarda los descuadres y devuelve el resultado.
    /// </summary>
    public async Task<ReconciliationRunDto> RunAsync(ReconciliationRunRequest? req, CancellationToken ct)
    {
        var pubs = (req?.ProductPublicIds ?? Array.Empty<Guid>()).Distinct().ToList();
        if (pubs.Count > ReconciliationRules.MaxManualProducts)
            throw new ValidationException("productPublicIds", ReconciliationRules.ManualTooManyProducts);
        if (pubs.Count == 0) return await SweepAsync(ReconciliationTriggers.Manual, ct);
        var ids = await db.Set<Product>().AsNoTracking().Where(p => pubs.Contains(p.PublicId)).Select(p => p.ProductId).ToListAsync(ct);
        if (ids.Count != pubs.Count) throw new NotFoundException("Producto");
        return await CheckProductsAsync(ids, ReconciliationTriggers.Manual, null, ct);
    }

    /// <summary>
    /// Revisión de productos concretos con el origen indicado (EVENT desde la revisión automática de P2, MANUAL, MIGRATION).
    /// lastTxnId = el movimiento que la disparó (se guarda en los descuadres que abre o actualiza).
    /// </summary>
    public Task<ReconciliationRunDto> CheckProductsAsync(IReadOnlyCollection<int> productIds, string trigger, long? lastTxnId, CancellationToken ct)
        => ReconcileAsync(productIds.Distinct().ToList(), trigger, lastTxnId, ct);

    /// <summary>
    /// Revisión de TODO el tenant con la misma lógica. Punto de enganche del barrido programado (SCHEDULED, fuera de este lote)
    /// y del cierre de import-legacy (MIGRATION).
    /// </summary>
    public Task<ReconciliationRunDto> SweepAsync(string trigger, CancellationToken ct) => ReconcileAsync(null, trigger, null, ct);

    private async Task<ReconciliationRunDto> ReconcileAsync(IReadOnlyList<int>? productIds, string trigger, long? lastTxnId, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("No hay tenant activo en la sesión.");
        var triggerId = await lookups.GetIdAsync(LookupDomains.ReconciliationTrigger, trigger, ct);
        var first = await reconciler.ComputeAsync(productIds, ct);

        var openQuery = db.InventoryDiscrepancies.AsNoTracking().Where(d => d.ClosedAtUtc == null);
        if (productIds is not null) openQuery = openQuery.Where(d => productIds.Contains(d.ProductId));
        var openProducts = await openQuery.Select(d => d.ProductId).Distinct().ToListAsync(ct);
        var candidates = first.Mismatches.Select(m => m.Key.ProductId).Concat(openProducts).Distinct().OrderBy(p => p).ToList();

        int opened = 0, stillOpen = 0, selfCorrected = 0;
        var confirmed = new List<ReconciliationMismatch>();
        foreach (var productId in candidates)
        {
            var outcome = await db.RunInTransactionAsync(ct2 => ConfirmProductAsync(tenantId, productId, triggerId, lastTxnId, ct2), ct);
            opened += outcome.Opened;
            stillOpen += outcome.StillOpen;
            selfCorrected += outcome.SelfCorrected;
            confirmed.AddRange(outcome.Mismatches);
        }
        if (db.Database.CurrentTransaction is null) db.ChangeTracker.Clear();   // dentro de una transacción ajena no se toca su tracker

        var rows = await reconciler.ToRowsAsync(confirmed, ct);
        return new ReconciliationRunDto(clock.UtcNow, productIds?.Count ?? first.ProductsSeen.Count, first.BalancesChecked, opened, stillOpen,
            selfCorrected, rows);
    }

    /// <summary>Confirma un producto bajo bloqueo y aplica el plan (paso 2 y 3). Corre dentro de su transacción.</summary>
    private async Task<ProductOutcome> ConfirmProductAsync(int tenantId, int productId, int triggerId, long? lastTxnId, CancellationToken ct)
    {
        await db.LockBalancesByProductAsync(productId, ct);
        var result = await reconciler.ComputeAsync(new[] { productId }, ct);

        var dismissedId = (await statuses.GetByCodeAsync(StatusDomains.InventoryDiscrepancyStatus, InventoryDiscrepancyStatuses.Dismissed, ct)).StatusCodeId;
        var stored = await db.InventoryDiscrepancies
            .Where(d => d.ProductId == productId && (d.ClosedAtUtc == null || d.StatusCodeId == dismissedId))
            .ToListAsync(ct);
        var kindCodes = await KindCodesAsync(stored.Select(d => d.KindLookupId), ct);
        StoredDiscrepancy ToStored(InventoryDiscrepancy d) => new(d.InventoryDiscrepancyId, kindCodes.GetValueOrDefault(d.KindLookupId) ?? string.Empty,
            KeyOf(d), d.LedgerQty, d.BalanceQty);
        var open = stored.Where(d => d.ClosedAtUtc == null).ToList();
        var plan = ReconciliationRules.Plan(open.Select(ToStored).ToList(), stored.Where(d => d.ClosedAtUtc != null).Select(ToStored).ToList(),
            result.Mismatches, new HashSet<int> { productId });
        if (plan.ToOpen.Count == 0 && plan.ToUpdate.Count == 0 && plan.ToSelfCorrect.Count == 0)
            return new ProductOutcome(0, 0, 0, result.Mismatches);

        var now = clock.UtcNow;
        var byId = open.ToDictionary(d => d.InventoryDiscrepancyId);
        foreach (var (id, m) in plan.ToUpdate)
        {
            var d = byId[id];
            d.LedgerQty = m.LedgerQty;
            d.BalanceQty = m.BalanceQty;
            d.LastCheckedAtUtc = now;
            d.CheckCount++;
            if (lastTxnId is long txn) d.LastTxnId = txn;
        }
        foreach (var id in plan.ToSelfCorrect)
        {
            var d = byId[id];
            var to = await statuses.TransitionAsync(StatusDomains.InventoryDiscrepancyStatus, EntityTypes.InventoryDiscrepancy, d.InventoryDiscrepancyId,
                d.StatusCodeId, InventoryDiscrepancyStatuses.SelfCorrected, ReconciliationRules.SelfCorrectedComment, ct);
            d.StatusCodeId = to.StatusCodeId;
            d.ClosedAtUtc = now;
            d.LastCheckedAtUtc = now;
            d.CheckCount++;
        }

        var born = new List<(InventoryDiscrepancy Entity, ReconciliationMismatch Mismatch)>();
        if (plan.ToOpen.Count > 0)
        {
            var initial = await statuses.GetInitialAsync(StatusDomains.InventoryDiscrepancyStatus, ct);
            foreach (var m in plan.ToOpen)
            {
                var d = new InventoryDiscrepancy
                {
                    PublicId = Guid.NewGuid(), TenantId = tenantId,
                    KindLookupId = await lookups.GetIdAsync(LookupDomains.InventoryDiscrepancyKind, m.KindCode, ct),
                    TriggerLookupId = triggerId, ProductId = m.Key.ProductId,
                    WarehouseId = m.ProductTotal ? null : m.Key.WarehouseId, WarehouseBinId = m.Key.BinId, LotId = m.Key.LotId,
                    LedgerQty = m.LedgerQty, BalanceQty = m.BalanceQty, DetectedAtUtc = now, LastCheckedAtUtc = now, CheckCount = 1,
                    LastTxnId = lastTxnId, StatusCodeId = initial.StatusCodeId,
                };
                db.InventoryDiscrepancies.Add(d);
                born.Add((d, m));
            }
            // Ids para el historial; UX_InvDiscrepancy_OpenKey es la última línea contra otra instancia que abriera el mismo.
            await db.SaveGuardedAsync(ConcurrentCheckMessage, ct);
            foreach (var (d, m) in born)
            {
                var to = await statuses.TransitionAsync(StatusDomains.InventoryDiscrepancyStatus, EntityTypes.InventoryDiscrepancy, d.InventoryDiscrepancyId,
                    null, initial.InternalCode, ReconciliationRules.OpenedComment(m.LedgerQty, m.BalanceQty), ct);
                d.StatusCodeId = to.StatusCodeId;
            }
        }
        await db.SaveGuardedAsync(ConcurrentCheckMessage, ct);
        return new ProductOutcome(plan.ToOpen.Count, plan.ToUpdate.Count, plan.ToSelfCorrect.Count, result.Mismatches);
    }

    /// <summary>
    /// Lote 14 (P2, D14) — estado de la revisión automática en segundo plano para la compañía de la sesión (contadores en memoria
    /// del servidor, por tenant). Sin cola registrada (pruebas) → apagada y sin pendientes.
    /// </summary>
    public ReconciliationStatusDto Status()
        => queue?.Status(tenant.TenantId) ?? new ReconciliationStatusDto(false, false, 0, 0, 0, null, null);

    // ================================================================ consultar

    /// <summary>
    /// Descuadres paginados (take ≤ 200, por defecto 50): abiertos primero y después por detección (más recientes primero).
    /// Filtros: estatus, almacenes, productos, categorías (con subcategorías), posiciones, tipo (400 si no existe) y fecha de
    /// detección en días locales. OpenCount = abiertos con los mismos filtros salvo el estatus.
    /// </summary>
    public async Task<InventoryDiscrepancyPageDto> ListAsync(InventoryDiscrepancyQuery? q, CancellationToken ct)
    {
        q ??= new InventoryDiscrepancyQuery();
        if (KardexRules.IsRangeInverted(q.From, q.To)) throw new ValidationException("to", KardexRules.RangeInverted);
        var (skip, take) = KardexRules.Page(q.Skip, q.Take, InventoryRules.MaxPageSize);
        var query = db.InventoryDiscrepancies.AsNoTracking().AsQueryable();

        if (q.WarehousePublicIds is { Length: > 0 })
        {
            var pubs = q.WarehousePublicIds.Distinct().ToList();
            var whIds = db.Warehouses.AsNoTracking().Where(w => pubs.Contains(w.PublicId)).Select(w => (int?)w.WarehouseId);
            query = query.Where(d => whIds.Contains(d.WarehouseId));
        }
        var productIds = await reads.FilteredProductIdsAsync(InventoryScope.Any, q.ProductPublicIds, q.CategoryIds, ct);
        if (productIds is not null) query = query.Where(d => productIds.Contains(d.ProductId));
        if (q.BinIds is { Length: > 0 })
        {
            var binIds = q.BinIds.Select(i => (int?)i).Distinct().ToList();
            query = query.Where(d => binIds.Contains(d.WarehouseBinId));
        }
        if (q.Kinds is { Length: > 0 })
        {
            var kindIds = new List<int>();
            foreach (var code in Codes(q.Kinds))
                kindIds.Add(await lookups.TryGetIdAsync(LookupDomains.InventoryDiscrepancyKind, code, ct)
                            ?? throw new ValidationException("kinds", UnknownKind(code)));
            query = query.Where(d => kindIds.Contains(d.KindLookupId));
        }
        var (fromUtc, toUtc) = clock.UtcRange(q.From, q.To);
        if (fromUtc is DateTime f) query = query.Where(d => d.DetectedAtUtc >= f);
        if (toUtc is DateTime t) query = query.Where(d => d.DetectedAtUtc < t);

        var openCount = await query.CountAsync(d => d.ClosedAtUtc == null, ct);
        if (q.Status is { Length: > 0 })
        {
            var codes = Codes(q.Status);
            var statusIds = await db.StatusCodes.AsNoTracking()
                .Where(s => s.Entity == StatusDomains.InventoryDiscrepancyStatus && codes.Contains(s.InternalCode))
                .Select(s => s.StatusCodeId).ToListAsync(ct);
            query = query.Where(d => statusIds.Contains(d.StatusCodeId));
        }

        var total = await query.CountAsync(ct);
        var page = await query.OrderBy(d => d.ClosedAtUtc == null ? 0 : 1).ThenByDescending(d => d.DetectedAtUtc)
            .ThenByDescending(d => d.InventoryDiscrepancyId).Skip(skip).Take(take).ToListAsync(ct);
        return new InventoryDiscrepancyPageDto(total, skip, take, openCount, await ToDtosAsync(page, ct));
    }

    /// <summary>
    /// Ficha: el descuadre, lo reservado hoy (de la clave o, en el total del producto, de todo el producto), los últimos 20
    /// movimientos de la clave (con la perspectiva de su posición) y el historial de estatus. Otro tenant → 404.
    /// </summary>
    public async Task<InventoryDiscrepancyDetailDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var d = await db.InventoryDiscrepancies.AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct)
                ?? throw new NotFoundException("Descuadre");
        var dto = (await ToDtosAsync(new[] { d }, ct))[0];
        var productId = d.ProductId;

        decimal? reserved;
        var txns = db.Set<InventoryTransaction>().AsNoTracking().Where(t => t.ProductId == productId);
        var filter = KardexLocationFilter.None;
        if (d.WarehouseId is int w)
        {
            var (bin, lot) = (d.WarehouseBinId, d.LotId);
            reserved = await db.StockBalances.AsNoTracking()
                .Where(b => b.ProductId == productId && b.WarehouseId == w && b.WarehouseBinId == bin && b.LotId == lot)
                .SumAsync(b => b.QtyReserved, ct);
            txns = txns.Where(t => t.LotId == lot
                                   && ((t.ToWarehouseId == w && t.ToBinId == bin) || (t.FromWarehouseId == w && t.FromBinId == bin)));
            filter = new KardexLocationFilter(new HashSet<int> { w }, bin is int b2 ? new HashSet<int> { b2 } : null);
        }
        else reserved = await db.StockBalances.AsNoTracking().Where(b => b.ProductId == productId).SumAsync(b => b.QtyReserved, ct);

        var recent = await txns.OrderByDescending(t => t.CreatedAtUtc).ThenByDescending(t => t.InventoryTransactionId)
            .Take(RecentMovementsCount).ToListAsync(ct);
        var movements = await reads.ToKardexRowsAsync(recent, filter, ct);
        var history = await statuses.GetHistoryAsync(EntityTypes.InventoryDiscrepancy, d.InventoryDiscrepancyId, ct);
        return new InventoryDiscrepancyDetailDto(dto, reserved, movements, history);
    }

    // ================================================================ resolver

    /// <summary>
    /// Resuelve un descuadre abierto (inventory.adjust):
    /// - 400: acción (REBUILD_BALANCE o DISMISS), nota ≤ 500, nota obligatoria al descartar.
    /// - 404 'Descuadre no encontrado.'; 409 si rowVersion no coincide ('El registro fue modificado por otro usuario; …').
    /// - 422: cerrado ('El descuadre ya está cerrado; solo se consulta.'); REBUILD sobre el total del producto.
    /// - REBUILD_BALANCE: el saldo toma lo que da el Kárdex bajo bloqueo (409 si el Kárdex da negativo o menos que lo
    ///   reservado); si ya cuadraba → SELF_CORRECTED (200); si no → RESOLVED con CorrectedFrom/To. DISMISS → DISMISSED.
    /// </summary>
    public async Task<InventoryDiscrepancyDetailDto> ResolveAsync(Guid publicId, DiscrepancyResolveRequest? req, CancellationToken ct)
    {
        var (action, notes, errors) = ReconciliationRules.ValidateResolveRequest(req?.Action, req?.Notes);
        // Un solo error (lo normal): el mensaje exacto también va en el detalle del 400, no solo en errors.
        if (errors.Count == 1) throw new ValidationException(errors.Keys.Single(), errors.Values.Single()[0]);
        if (errors.Count > 0) throw new ValidationException(errors);
        var snapshot = await db.InventoryDiscrepancies.AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct)
                       ?? throw new NotFoundException("Descuadre");

        await db.RunInTransactionAsync(async ct2 =>
        {
            var d = await db.InventoryDiscrepancies.FirstOrDefaultAsync(x => x.InventoryDiscrepancyId == snapshot.InventoryDiscrepancyId, ct2)
                    ?? throw new NotFoundException("Descuadre");
            EnsureRowVersion(d, req?.RowVersion);
            var kind = (await lookups.GetAsync(d.KindLookupId, ct2))?.InternalCode ?? DiscrepancyKinds.Balance;
            var blocker = ReconciliationRules.ResolveBlocker(d.ClosedAtUtc is not null, kind, action!);
            if (blocker is not null) throw new StatusRuleException(blocker);

            var now = clock.UtcNow;
            string target;
            var comment = notes;
            if (action == ReconciliationRules.ActionDismiss) target = InventoryDiscrepancyStatuses.Dismissed;
            else
            {
                var rebuilt = await ledger.RebuildBalanceAsync(KeyOf(d), ct2);
                if (rebuilt.Changed)
                {
                    target = InventoryDiscrepancyStatuses.Resolved;
                    d.CorrectedFromQty = rebuilt.Before;
                    d.CorrectedToQty = rebuilt.After;
                }
                else
                {
                    target = InventoryDiscrepancyStatuses.SelfCorrected;
                    comment ??= ReconciliationRules.AlreadyBalancedComment;
                }
            }
            var to = await statuses.TransitionAsync(StatusDomains.InventoryDiscrepancyStatus, EntityTypes.InventoryDiscrepancy, d.InventoryDiscrepancyId,
                d.StatusCodeId, target, comment, ct2);
            d.StatusCodeId = to.StatusCodeId;
            d.ClosedAtUtc = now;
            d.LastCheckedAtUtc = now;
            d.ResolvedBy = tenant.UserId;
            d.ResolutionNotes = notes;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);

        if (db.Database.CurrentTransaction is null) db.ChangeTracker.Clear();   // dentro de una transacción ajena no se toca su tracker
        return await GetAsync(publicId, ct);
    }

    // ================================================================ apoyo

    private static BalanceKey KeyOf(InventoryDiscrepancy d) => new(d.ProductId, d.WarehouseId ?? 0, d.WarehouseBinId, d.LotId);

    private static List<string> Codes(IEnumerable<string> values)
        => values.SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(v => v.ToUpperInvariant()).Distinct().ToList();

    private async Task<Dictionary<int, string>> KindCodesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var map = new Dictionary<int, string>();
        foreach (var id in ids.Distinct())
            if (await lookups.GetAsync(id, ct) is { } lc) map[id] = lc.InternalCode;
        return map;
    }

    /// <summary>
    /// RowVersion (base64 de la ficha) mal formado → 400; distinto del actual → 409; vacío = sin control (como el resto del
    /// WMS). El UPDATE final lleva además el RowVersion original como token de concurrencia (SaveGuardedAsync → 409).
    /// </summary>
    private static void EnsureRowVersion(InventoryDiscrepancy d, string? rowVersionBase64)
    {
        if (string.IsNullOrWhiteSpace(rowVersionBase64)) return;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(rowVersionBase64.Trim()); }
        catch (FormatException) { throw new ValidationException("rowVersion", "rowVersion inválido: se espera el valor base64 devuelto por la ficha."); }
        if (d.RowVersion is not { Length: > 0 } current) return;
        if (bytes.Length > 0 && !bytes.AsSpan().SequenceEqual(current)) throw new ConflictException(DbExtensions.ConcurrencyMessage);
    }

    /// <summary>Filas a DTO en el orden de entrada; catálogos por lotes (sin N+1).</summary>
    private async Task<IReadOnlyList<InventoryDiscrepancyDto>> ToDtosAsync(IReadOnlyList<InventoryDiscrepancy> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return Array.Empty<InventoryDiscrepancyDto>();
        var products = await reads.ProductInfoAsync(rows.Select(r => r.ProductId).Distinct().ToList(), ct);
        var warehouses = await reads.WarehouseInfoAsync(rows.Where(r => r.WarehouseId.HasValue).Select(r => r.WarehouseId!.Value), ct);
        var bins = await reads.BinInfoAsync(rows.Where(r => r.WarehouseBinId.HasValue).Select(r => r.WarehouseBinId!.Value), ct);
        var lots = await reads.LotInfoAsync(rows.Where(r => r.LotId.HasValue).Select(r => r.LotId!.Value), ct);
        var statusMap = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.InventoryDiscrepancyStatus)
            .ToDictionaryAsync(s => s.StatusCodeId, ct);
        var statusIds = statusMap.Keys.ToList();
        var overrides = await db.StatusCodeOverrides.AsNoTracking().Where(o => statusIds.Contains(o.StatusCodeId))
            .ToDictionaryAsync(o => o.StatusCodeId, o => o.CustomLabelJson, ct);
        var lookupLabels = new Dictionary<int, (string Code, string Label)>();
        foreach (var id in rows.SelectMany(r => new[] { r.KindLookupId, r.TriggerLookupId }).Distinct())
            if (await lookups.GetAsync(id, ct) is { } lc) lookupLabels[id] = (lc.InternalCode, MultilingualText.Resolve(lc.LabelJson, tenant.Lang));
        var userIds = rows.Where(r => r.ResolvedBy.HasValue).Select(r => r.ResolvedBy!.Value).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FullName ?? u.Email ?? u.UserName ?? "" })
                .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var result = new List<InventoryDiscrepancyDto>(rows.Count);
        foreach (var d in rows)
        {
            var p = products.GetValueOrDefault(d.ProductId);
            var w = d.WarehouseId is int wid ? warehouses.GetValueOrDefault(wid) : null;
            var st = statusMap.GetValueOrDefault(d.StatusCodeId);
            var statusLabel = st is null ? string.Empty
                : MultilingualText.Resolve(MultilingualText.Merge(st.LabelJson, overrides.GetValueOrDefault(d.StatusCodeId)), tenant.Lang);
            var kind = lookupLabels.GetValueOrDefault(d.KindLookupId);
            var trigger = lookupLabels.GetValueOrDefault(d.TriggerLookupId);
            result.Add(new InventoryDiscrepancyDto(d.PublicId, kind.Code ?? string.Empty, kind.Label ?? string.Empty,
                p?.PublicId ?? Guid.Empty, p?.Sku ?? string.Empty, p?.Name ?? string.Empty,
                w?.PublicId, w?.Code, d.WarehouseBinId, d.WarehouseBinId is int bid ? bins.GetValueOrDefault(bid)?.Code : null,
                d.LotId, d.LotId is int lid ? lots.GetValueOrDefault(lid)?.Number : null,
                d.LedgerQty, d.BalanceQty, ReconciliationRules.Difference(d.LedgerQty, d.BalanceQty),
                st?.InternalCode ?? string.Empty, statusLabel, trigger.Code ?? string.Empty, trigger.Label ?? string.Empty,
                d.DetectedAtUtc, d.LastCheckedAtUtc, d.CheckCount, d.ClosedAtUtc,
                d.ResolvedBy is int uid ? users.GetValueOrDefault(uid) : null, d.ResolutionNotes, d.CorrectedFromQty, d.CorrectedToQty,
                d.LastTxnId, Convert.ToBase64String(d.RowVersion ?? Array.Empty<byte>())));
        }
        return result;
    }
}
