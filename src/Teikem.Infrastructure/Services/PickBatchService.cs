using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P7) — Recolección y empaque ad hoc (R13, R34-R43, R11; D10-D15, D35, D37-D39, D43, D48).
///
/// Recolectar (CollectAsync, D48):
///   1. la fila del contador PACKBATCH se asegura FUERA de la transacción (EnsureAsync, autocommit);
///   2. dentro de RunInTransactionAsync: almacén, productos activos de UN solo dueño (D15) y asignación por línea SIN
///      bloqueo (series AVAILABLE por su posición actual, posición/lote explícitos validados, o FEFO sobre el disponible =
///      en mano − reservado, D14);
///   3. NextAsync(PACKBATCH) es el PRIMER bloqueo: excepción documentada al orden de bloqueo del lote (InventoryQueries);
///      no hay ciclo porque nadie bloquea saldos y después PACKBATCH;
///   4. se inserta la cabecera COLLECTED con su número EMP-##### (historial PICK_BATCH null → COLLECTED);
///   5. InventoryLedger.PostAsync con ISSUE desde la posición y Ref PICK_BATCH + id YA en el INSERT (nadie hace UPDATE del
///      ledger). El ledger bloquea los saldos en orden de clave y re-verifica: un faltante es 409 insufficient_stock y revierte
///      TODO, incluido el número (los EMP quedan consecutivos y sin huecos, D37);
///   6. líneas con el costo congelado (UnitCost = Product.PurchaseCost, D35) y su IssueTxnId.
///
/// Empacar (PackAsync, D11): warehouse.pick (controlador) + orders.create; bloquea la recolección y re-verifica COLLECTED;
/// crea la orden REAL con OrderService.CreateAsync (misma transacción; número de empaque = número de la recolección, origen
/// PICK_BATCH; la numeración de orden y factura respeta ClientAssignsOrderNumber/ClientAssignsInvoiceNumber del cliente); copia
/// la factura final al lote (R40) y pasa COLLECTED → PACKED. UX_PickBatch_Order es la última línea en BD.
///
/// Eliminar (DeleteAsync, D12/D13): bloquea la recolección (404 al segundo de dos); si está PACKED exige orders.cancel y que
/// su orden siga activa y en la etapa inicial (422 si ya avanzó) y la borra por OrderService con OrderDeletionOptions
/// (PICK_BATCH); revierte cada línea con un ADJUSTMENT de entrada PICK_BATCH_REVERSAL a su posición original (422 si está
/// inactiva; la serie vuelve a AVAILABLE; el ISSUE original no se toca) y pasa a CANCELLED con IsActive = 0.
///
/// Despacho manual (ManualIssueAsync, 2026-10-11): la MISMA recolección (mismo CollectCoreAsync: FEFO, series, un solo dueño,
/// ISSUE con Ref PICK_BATCH, 409 insufficient_stock sin efecto parcial) con motivo obligatorio del catálogo ManualIssueReason,
/// nota libre (≤ 500) y número DMA-##### de su propio contador (MANUALISSUE; no consume PACKBATCH). La nota de cada ISSUE del
/// Kárdex es 'DMA-00012 · {motivo}[ · {nota}]'. Nunca se empaca (422) y collect-and-pack nunca lo crea. Eliminar: como una
/// recolección COLLECTED (reversa a la posición original), con warehouse.issue en lugar de orders.cancel y sin orden.
///
/// El TenantId sale del principal; la recolección se expone por PublicId; posiciones y lotes (hijas sin TenantId) se
/// resuelven SIEMPRE por su almacén o producto filtrados (otra posición → 404 sin oráculo).
/// </summary>
public sealed class PickBatchService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    INumberSequenceService numbers,
    PermissionService permissions,
    InventoryLedger ledger,
    OrderService orders)
{
    public const string PickBatchLabel = "Recolección";

    /// <summary>Etiqueta del 404 de las rutas de despacho manual ('Despacho manual no encontrado.').</summary>
    public const string ManualIssueLabel = PickBatchRules.ManualIssueLabel;

    /// <summary>El contador PACKBATCH es uno por tenant (ClientId NULL, NumberingRules.ScopeClientId) y lo comparten las órdenes (D10).</summary>
    private static readonly int? PackBatchCounterClientId = null;

    // ---------------------------------------------------------------- tipos internos

    private sealed record ProductInfo(int ProductId, Guid PublicId, string Sku, bool IsActive, int? ClientId, string TrackingCode, decimal? PurchaseCost);

    private sealed record BinInfo(int BinId, string Code, bool Active, string? ZoneTypeCode);

    /// <summary>Movimiento planeado de una línea: una fila de ISSUE y una fila de PickBatchLine.</summary>
    private sealed record PlannedPick(ProductInfo Product, int BinId, int? LotId, int? SerialId, string? SerialNumber, decimal Quantity);

    private sealed record SerialRow(int SerialId, string SerialNumber, int? LotId, int? StatusCodeId, int? CurrentWarehouseId, int? CurrentBinId);

    /// <summary>Datos del despacho manual que viajan a CollectCoreAsync: motivo (id y etiqueta resuelta) y nota normalizada.</summary>
    private sealed record ManualIssueSpec(int ReasonId, string ReasonLabel, string? Note);

    // ================================================================ lista

    public async Task<PickBatchPageDto> ListAsync(PickBatchQuery? q, CancellationToken ct)
    {
        q ??= new PickBatchQuery();
        var (skip, take) = PickBatchRules.NormalizePaging(q.Skip, q.Take);
        var (kind, kindError) = PickBatchRules.NormalizeKind(q.Kind);
        if (kindError is not null) throw new ValidationException("kind", kindError);

        // (1) filtros estructurales en SQL: tipo (2026-10-11), fechas (UTC, 'hasta' inclusive), estatus, eliminadas y productos.
        var query = db.Set<PickBatch>().AsNoTracking();
        if (kind == PickBatchRules.KindManual) query = query.Where(b => b.ManualIssueReasonId != null);
        else if (kind == PickBatchRules.KindPack) query = query.Where(b => b.ManualIssueReasonId == null);
        if (!q.IncludeDeleted) query = query.Where(b => b.IsActive);
        if (q.From is DateOnly from)
        {
            var f = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(b => b.CollectedAtUtc >= f);
        }
        if (q.To is DateOnly to)
        {
            var t = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(b => b.CollectedAtUtc < t);
        }
        if (q.Status is { Length: > 0 })
        {
            var codes = q.Status.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).Distinct().ToList();
            var ids = await db.StatusCodes.AsNoTracking()
                .Where(s => s.Entity == StatusDomains.PickBatchStatus && codes.Contains(s.InternalCode))
                .Select(s => s.StatusCodeId).ToListAsync(ct);
            query = query.Where(b => ids.Contains(b.StatusCodeId));
        }
        if (q.ProductPublicIds is { Length: > 0 })
        {
            var pids = q.ProductPublicIds.Distinct().ToList();
            query = query.Where(b => (from l in db.Set<PickBatchLine>()
                                      join p in db.Set<Product>() on l.ProductId equals p.ProductId
                                      where l.PickBatchId == b.PickBatchId && pids.Contains(p.PublicId)
                                      select l.PickBatchLineId).Any());
        }

        // (2) filas ligeras del conjunto filtrado: filtros de texto (orden, factura) y DESPUÉS la búsqueda final (R41).
        var light = await (from b in query
                           join o in db.TransportOrders.AsNoTracking() on b.TransportOrderId equals (int?)o.TransportOrderId into oj
                           from o in oj.DefaultIfEmpty()
                           join c in db.Clients.AsNoTracking() on o.ClientId equals c.ClientId into cj
                           from c in cj.DefaultIfEmpty()
                           select new
                           {
                               b.PickBatchId, b.Number, b.CollectedAtUtc, b.ClientInvoiceNumber, b.ManualIssueReasonId, b.Note,
                               OrderNumber = o == null ? null : o.OrderNumber,
                               PackBatchNumber = o == null ? null : o.PackBatchNumber,
                               OrderInvoice = o == null ? null : o.ClientInvoiceNumber,
                               ClientName = c == null ? null : c.Name,
                           }).ToListAsync(ct);

        Dictionary<int, List<string>> skus = new();
        if (!string.IsNullOrWhiteSpace(q.Search) && light.Count > 0)
        {
            var ids = light.Select(x => x.PickBatchId).ToList();
            skus = (await (from l in db.Set<PickBatchLine>().AsNoTracking()
                           join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                           where ids.Contains(l.PickBatchId)
                           select new { l.PickBatchId, p.Sku }).ToListAsync(ct))
                .GroupBy(x => x.PickBatchId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Sku).Distinct().ToList());
        }

        var reasons = string.IsNullOrWhiteSpace(q.Search)
            ? new Dictionary<int, (string Code, string Label)>()
            : await ReasonsAsync(light.Where(x => x.ManualIssueReasonId != null).Select(x => x.ManualIssueReasonId!.Value), ct);
        var rows = light.Select(x => new PickBatchListRow(x.PickBatchId, x.Number, x.OrderNumber, x.PackBatchNumber,
                x.ClientInvoiceNumber ?? x.OrderInvoice, x.ClientName,
                (IReadOnlyCollection<string>?)skus.GetValueOrDefault(x.PickBatchId) ?? Array.Empty<string>(),
                x.ManualIssueReasonId is int rid && reasons.TryGetValue(rid, out var r) ? r.Label : null, x.Note))
            .ToList();
        var matched = PickBatchRules.FilterThenSearch(rows, q.OrderNumber, q.InvoiceNumber, q.Search)
            .Select(r => r.PickBatchId).ToHashSet();

        var ordered = light.Where(x => matched.Contains(x.PickBatchId))
            .OrderByDescending(x => x.CollectedAtUtc).ThenByDescending(x => x.PickBatchId)
            .Select(x => x.PickBatchId).ToList();
        var pageIds = ordered.Skip(skip).Take(take).ToList();
        var dtos = await BuildDtosAsync(pageIds, ct);
        return new PickBatchPageDto(ordered.Count, skip, take, pageIds.Where(dtos.ContainsKey).Select(id => dtos[id]).ToList());
    }

    // ================================================================ ficha

    /// <summary>Ficha por PublicId (también de una recolección eliminada: queda para consulta con su reversa).</summary>
    public async Task<PickBatchDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var id = await db.Set<PickBatch>().AsNoTracking().Where(b => b.PublicId == publicId)
                     .Select(b => (int?)b.PickBatchId).FirstOrDefaultAsync(ct)
                 ?? throw NotFound();
        return (await BuildDtosAsync(new[] { id }, ct))[id];
    }

    // ================================================================ recolectar (D48)

    /// <summary>Recolecta (sin empacar). Con 'pack' → 400: la variante atómica es CollectAndPackAsync (ruta collect-and-pack).</summary>
    public async Task<PickBatchDto> CollectAsync(PickBatchCreateRequest req, CancellationToken ct)
    {
        if (req?.Pack is not null) throw new ValidationException("pack", PickBatchRules.PackUseCollectAndPack);
        return await GetAsync(await CollectCoreAsync(req!, ct), ct);
    }

    /// <summary>
    /// Lote 8A (cola del aparato): recolectar y empacar en UNA transacción con exactamente los mismos pasos, permisos y mensajes
    /// que CollectAsync + PackAsync. Si el empaque falla (permiso, orden inválida, dueño 3PL) la recolección tampoco queda: el
    /// inventario no sale y el número EMP no se consume. El RowVersion del empaque se ignora (la recolección nace aquí).
    /// </summary>
    public async Task<PickBatchPackResultDto> CollectAndPackAsync(PickBatchCreateRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.Pack is null) throw new ValidationException("pack", PickBatchRules.OrderRequired);
        var pack = req.Pack with { RowVersion = null };
        // Permiso y forma del empaque ANTES de sacar inventario (mismo orden de mensajes que empacar por separado).
        await ValidatePackRequestAsync(pack, ct);
        // Fila del contador PACKBATCH en autocommit antes de la transacción externa (la llamada anidada no inserta nada).
        await numbers.EnsureAsync(NumberKinds.PackBatch, PackBatchCounterClientId, ct);

        var (publicId, order) = await db.RunInTransactionAsync(async ct2 =>
        {
            // Las transacciones internas de recolección y empaque se unen a ésta (RunInTransactionAsync anidado).
            var collected = await CollectCoreAsync(req with { Pack = null }, ct2);
            var created = await PackCoreAsync(collected, pack, ct2);
            return (collected, created);
        }, ct);
        return new PickBatchPackResultDto(await GetAsync(publicId, ct), order);
    }

    // ================================================================ despacho manual (2026-10-11)

    /// <summary>
    /// Despacho manual: valida motivo (obligatorio, del catálogo ManualIssueReason activo y habilitado para la compañía) y nota
    /// (≤ 500) ANTES de tocar inventario, y sale por el mismo CollectCoreAsync con número DMA-#####.
    /// </summary>
    public async Task<PickBatchDto> ManualIssueAsync(ManualIssueCreateRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var errors = new Dictionary<string, string[]>();
        var (reasonCode, reasonError) = PickBatchRules.NormalizeReason(req.ReasonCode);
        if (reasonError is not null) errors["reasonCode"] = new[] { reasonError };
        var (note, noteError) = PickBatchRules.NormalizeNote(req.Note);
        if (noteError is not null) errors["note"] = new[] { noteError };
        if (errors.Count == 1) { var e = errors.First(); throw new ValidationException(e.Key, e.Value[0]); }
        if (errors.Count > 1) throw new ValidationException(errors);

        var reason = await ResolveManualReasonAsync(reasonCode!, ct)
                     ?? throw new ValidationException("reasonCode", PickBatchRules.ManualReasonUnknown(reasonCode!));
        var publicId = await CollectCoreAsync(new PickBatchCreateRequest(req.WarehousePublicId, req.Lines), ct,
            new ManualIssueSpec(reason.Id, reason.Label, note));
        return await GetAsync(publicId, ct);
    }

    /// <summary>Ficha de un despacho manual (404 'Despacho manual no encontrado.' si el PublicId es de una recolección EMP).</summary>
    public async Task<PickBatchDto> GetManualIssueAsync(Guid publicId, CancellationToken ct)
    {
        var current = await db.Set<PickBatch>().AsNoTracking().Where(b => b.PublicId == publicId)
            .Select(b => new { b.PickBatchId, b.ManualIssueReasonId }).FirstOrDefaultAsync(ct);
        if (current is null || !PickBatchRules.IsManual(current.ManualIssueReasonId)) throw ManualNotFound();
        return (await BuildDtosAsync(new[] { current.PickBatchId }, ct))[current.PickBatchId];
    }

    /// <summary>
    /// Elimina un despacho manual: mismo DeleteAsync con reversa. 404 'Despacho manual no encontrado.' si el PublicId es de una
    /// recolección EMP, no existe o ya fue eliminado (también el segundo de dos DELETE simultáneos).
    /// </summary>
    public async Task DeleteManualIssueAsync(Guid publicId, PickBatchDeleteRequest? req, CancellationToken ct)
    {
        var current = await db.Set<PickBatch>().AsNoTracking().Where(b => b.PublicId == publicId)
            .Select(b => new { b.ManualIssueReasonId }).FirstOrDefaultAsync(ct);
        if (current is null || !PickBatchRules.IsManual(current.ManualIssueReasonId)) throw ManualNotFound();
        try
        {
            await DeleteAsync(publicId, req, ct);
        }
        catch (NotFoundException)
        {
            throw ManualNotFound();
        }
    }

    /// <summary>
    /// Motivo por código: activo, visible para la compañía (global o propio, filtro de tenant) y no deshabilitado por su
    /// override; etiqueta en el idioma del usuario con el override aplicado. NULL = no existe o está inactivo.
    /// </summary>
    private async Task<(int Id, string Label)?> ResolveManualReasonAsync(string code, CancellationToken ct)
    {
        var row = await db.LookupCodes.AsNoTracking()
            .Where(l => l.Entity == LookupDomains.ManualIssueReason && l.InternalCode == code && l.IsActive)
            .Select(l => new { l.LookupCodeId, l.LabelJson }).FirstOrDefaultAsync(ct);
        if (row is null) return null;
        var ov = await db.LookupCodeOverrides.AsNoTracking().Where(o => o.LookupCodeId == row.LookupCodeId)
            .Select(o => new { o.IsEnabled, o.CustomLabelJson }).FirstOrDefaultAsync(ct);
        if (ov is { IsEnabled: false }) return null;
        return (row.LookupCodeId, MultilingualText.Resolve(MultilingualText.Merge(row.LabelJson, ov?.CustomLabelJson), tenant.Lang));
    }

    /// <summary>
    /// Código y etiqueta (override de la compañía aplicado) de los motivos dados; también los ya inactivos o deshabilitados
    /// (historial). Se lee de la base (no de la caché): un motivo recién agregado en Sistema → Catálogos se ve de inmediato.
    /// </summary>
    private async Task<Dictionary<int, (string Code, string Label)>> ReasonsAsync(IEnumerable<int> reasonIds, CancellationToken ct)
    {
        var ids = reasonIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, (string Code, string Label)>();
        var rows = await db.LookupCodes.AsNoTracking().Where(l => ids.Contains(l.LookupCodeId))
            .Select(l => new { l.LookupCodeId, l.InternalCode, l.LabelJson }).ToListAsync(ct);
        var overrides = await db.LookupCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.LookupCodeId))
            .Select(o => new { o.LookupCodeId, o.CustomLabelJson }).ToDictionaryAsync(o => o.LookupCodeId, o => o.CustomLabelJson, ct);
        return rows.ToDictionary(l => l.LookupCodeId, l => (l.InternalCode,
            MultilingualText.Resolve(MultilingualText.Merge(l.LabelJson, overrides.GetValueOrDefault(l.LookupCodeId)), tenant.Lang)));
    }

    private static NotFoundException ManualNotFound() => new(ManualIssueLabel);

    /// <summary>
    /// Núcleo de la salida (recolección o despacho manual). manual != null = despacho manual: contador MANUALISSUE (DMA-#####),
    /// motivo y nota en la cabecera y nota 'DMA-… · motivo' en cada ISSUE; todo lo demás es idéntico.
    /// </summary>
    private async Task<Guid> CollectCoreAsync(PickBatchCreateRequest req, CancellationToken ct, ManualIssueSpec? manual = null)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var lines = req.Lines ?? Array.Empty<PickBatchLineRequest>();

        // Validación de forma (400 con errores por línea) antes de tocar la BD.
        var errors = new Dictionary<string, string[]>();
        if (lines.Count == 0) errors["lines"] = new[] { PickBatchRules.LinesRequired };
        else if (lines.Count > PickBatchRules.MaxLines) errors["lines"] = new[] { PickBatchRules.TooManyLines };
        var serialsByLine = new List<IReadOnlyList<string>>(lines.Count);
        var seenSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line is null) { errors[$"lines[{i}]"] = new[] { PickBatchRules.ProductRequired }; serialsByLine.Add(Array.Empty<string>()); continue; }
            if (line.ProductPublicId is null) errors[$"lines[{i}].productPublicId"] = new[] { PickBatchRules.ProductRequired };
            var (serials, serialError) = PickBatchRules.NormalizeSerials(line.SerialNumbers);
            if (serialError is not null) errors[$"lines[{i}].serialNumbers"] = new[] { serialError };
            foreach (var s in serials)
                if (!seenSerials.Add(s)) errors[$"lines[{i}].serialNumbers"] = new[] { PickBatchRules.SerialDuplicated(s) };
            // Sin series la cantidad es obligatoria; con series se valida contra su número (abajo, según el seguimiento).
            if (serials.Count == 0 && PickBatchRules.ValidateQuantity(line.Quantity) is string qtyError)
                errors[$"lines[{i}].quantity"] = new[] { qtyError };
            serialsByLine.Add(serials);
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        // (1) fila del contador (PACKBATCH, o MANUALISSUE en un despacho manual) en autocommit (idempotente); el valor se consume
        //     dentro de la transacción.
        var counterKind = manual is null ? NumberKinds.PackBatch : NumberKinds.ManualIssue;
        var numberTaken = manual is null ? PickBatchRules.NumberTaken : PickBatchRules.ManualNumberTaken;
        await numbers.EnsureAsync(counterKind, PackBatchCounterClientId, ct);

        var publicId = await db.RunInTransactionAsync(async ct2 =>
        {
            // (2a) almacén, productos y asignación SIN bloqueo (el ledger bloquea y re-verifica después)
            var warehouse = await ResolveWarehouseOrDefaultAsync(req.WarehousePublicId, ct2);
            if (!warehouse.IsActive) throw new StatusRuleException(PickBatchRules.WarehouseInactive);
            var products = await ProductsAsync(lines.Select(l => l.ProductPublicId!.Value), ct2);
            foreach (var p in products.Values)
                if (!p.IsActive) throw new StatusRuleException(PickBatchRules.ProductInactive(p.Sku));
            if (!PickBatchRules.IsSingleOwner(products.Values.Select(p => p.ClientId)))
                throw new ValidationException("lines", manual is null ? PickBatchRules.SingleOwner : PickBatchRules.ManualSingleOwner);

            var plan = await PlanAsync(warehouse, lines, serialsByLine, products, ct2);

            // (2b) número EMP (o DMA): PRIMER bloqueo de la transacción (excepción D48 al orden del lote)
            var seq = await numbers.NextAsync(counterKind, PackBatchCounterClientId, ct2);
            var number = manual is null
                ? NumberingRules.Resolve(NumberingRules.PackBatchPattern, seq)
                : WmsNumbering.Format(NumberKinds.ManualIssue, seq);

            // (2c) cabecera COLLECTED ANTES de los ISSUE: el Ref PICK_BATCH + id va en el INSERT del ledger
            var initial = await statuses.GetInitialAsync(StatusDomains.PickBatchStatus, ct2);
            var batch = new PickBatch
            {
                PublicId = Guid.NewGuid(),
                TenantId = tenantId,
                WarehouseId = warehouse.WarehouseId,
                Number = number,
                StatusCodeId = initial.StatusCodeId,
                CollectedAtUtc = DateTime.UtcNow,
                CollectedBy = tenant.UserId,
                IsActive = true,
                ManualIssueReasonId = manual?.ReasonId,
                Note = manual?.Note,
            };
            db.Set<PickBatch>().Add(batch);
            await db.SaveGuardedAsync(numberTaken, ct2);
            var born = await statuses.TransitionAsync(StatusDomains.PickBatchStatus, EntityTypes.PickBatch, batch.PickBatchId, null,
                initial.InternalCode, null, ct2);
            batch.StatusCodeId = born.StatusCodeId;

            // (2d) ISSUE por porción, desde la posición, con Ref PICK_BATCH + id desde el INSERT (D3: el ledger pone el signo −).
            //      Despacho manual: la nota del Kárdex lleva 'DMA-00012 · {motivo}'.
            var movementNote = manual is null ? null : PickBatchRules.ManualIssueMovementNote(number, manual.ReasonLabel);
            var postings = plan.Select(x => new InventoryPosting(InventoryTxnTypes.Issue, x.Product.ProductId, x.Quantity,
                    LotId: x.LotId, SerialId: x.SerialId, SerialNumber: x.SerialNumber,
                    FromWarehouseId: warehouse.WarehouseId, FromBinId: x.BinId,
                    RefEntityType: EntityTypes.PickBatch, RefId: batch.PickBatchId, Notes: movementNote))
                .ToList();
            var txnIds = await ledger.PostAsync(postings, ct2);

            // (2e) líneas con el costo congelado (D35) y su movimiento
            for (var i = 0; i < plan.Count; i++)
            {
                var x = plan[i];
                db.Set<PickBatchLine>().Add(new PickBatchLine
                {
                    PickBatchId = batch.PickBatchId,
                    ProductId = x.Product.ProductId,
                    LotId = x.LotId,
                    SerialId = x.SerialId,
                    FromBinId = x.BinId,
                    Quantity = x.Quantity,
                    UnitCost = x.Product.PurchaseCost,
                    IssueTxnId = txnIds[i],
                });
            }
            await db.SaveGuardedAsync(numberTaken, ct2);
            return batch.PublicId;
        }, ct);

        return publicId;
    }

    /// <summary>
    /// Asignación de todas las líneas sobre una foto SIN bloqueo del disponible (en mano − reservado). Varias líneas del mismo
    /// producto consumen de la misma foto (no se asigna dos veces el mismo saldo). Un faltante → 409 insufficient_stock con
    /// errores por línea y sin escribir nada; el ledger vuelve a verificar con los saldos bloqueados.
    /// </summary>
    private async Task<List<PlannedPick>> PlanAsync(Warehouse warehouse, IReadOnlyList<PickBatchLineRequest> lines,
        IReadOnlyList<IReadOnlyList<string>> serialsByLine, IReadOnlyDictionary<Guid, ProductInfo> products, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var shortages = new Dictionary<string, string[]>();
        var productIds = products.Values.Select(p => p.ProductId).ToList();

        // Posiciones del almacén (con tipo de zona) y saldos de los productos en posiciones del almacén.
        var bins = await BinsOfWarehouseAsync(warehouse.WarehouseId, ct);
        var balances = await (from b in db.Set<StockBalance>().AsNoTracking()
                              join lot in db.Set<InventoryLot>().AsNoTracking() on b.LotId equals (int?)lot.LotId into lj
                              from lot in lj.DefaultIfEmpty()
                              where b.WarehouseId == warehouse.WarehouseId && productIds.Contains(b.ProductId)
                                    && b.WarehouseBinId != null && b.QtyOnHand > 0
                              select new
                              {
                                  b.ProductId, BinId = b.WarehouseBinId!.Value, b.LotId, b.QtyOnHand, b.QtyReserved,
                                  Expiry = lot == null ? (DateOnly?)null : lot.ExpiryDate,
                              }).ToListAsync(ct);
        // Disponible restante por saldo (producto, posición, lote): lo que las líneas anteriores ya tomaron se descuenta.
        var remaining = balances.ToDictionary(b => (b.ProductId, b.BinId, b.LotId), b => Math.Max(0m, b.QtyOnHand - b.QtyReserved));
        var expiries = balances.ToDictionary(b => (b.ProductId, b.BinId, b.LotId), b => b.Expiry);

        var serialStatus = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.SerialStatus && s.InternalCode == SerialStatuses.Available)
            .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);

        var plan = new List<PlannedPick>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var product = products[line.ProductPublicId!.Value];
            var serials = serialsByLine[i];
            var isSerial = string.Equals(product.TrackingCode, TrackingTypes.Serial, StringComparison.OrdinalIgnoreCase);
            var isNone = string.Equals(product.TrackingCode, TrackingTypes.None, StringComparison.OrdinalIgnoreCase);

            // Posición y lote explícitos: se resuelven por su padre filtrado (almacén / producto); ajenos → 404.
            BinInfo? explicitBin = null;
            if (line.BinId is int binId)
            {
                explicitBin = bins.GetValueOrDefault(binId) ?? throw new NotFoundException("Posición", feminine: true);
                if (!explicitBin.Active) throw new StatusRuleException(PickBatchRules.BinInactive(explicitBin.Code));
                if (!PickBatchRules.IsPickableZone(explicitBin.ZoneTypeCode))
                    throw new StatusRuleException(PickBatchRules.ZoneNotPickable(explicitBin.Code, explicitBin.ZoneTypeCode!));
            }
            if (line.LotId is int lotId)
            {
                if (isNone) { errors[$"lines[{i}].lotId"] = new[] { PickBatchRules.LotNotTracked(product.Sku) }; continue; }
                _ = await ResolveLotAsync(product.ProductId, lotId, ct);
            }

            if (isSerial)
            {
                if (serials.Count == 0) { errors[$"lines[{i}].serialNumbers"] = new[] { PickBatchRules.SerialRequired(product.Sku) }; continue; }
                var (_, qtyError) = PickBatchRules.SerialQuantity(line.Quantity, serials.Count);
                if (qtyError is not null) { errors[$"lines[{i}].quantity"] = new[] { qtyError }; continue; }

                var found = await SerialsAsync(product.ProductId, serials, ct);
                foreach (var number in serials)
                {
                    var s = found.GetValueOrDefault(number);
                    var bin = s?.CurrentBinId is int cb ? bins.GetValueOrDefault(cb) : null;
                    // Disponible = AVAILABLE, en este almacén, en una posición activa y recolectable, y (si se indicó) en esa
                    // posición y lote. Lo reservado del saldo lo re-verifica el ledger.
                    var ok = s is not null && serialStatus is not null && s.StatusCodeId == serialStatus
                             && s.CurrentWarehouseId == warehouse.WarehouseId && bin is not null && bin.Active
                             && PickBatchRules.IsPickableZone(bin.ZoneTypeCode)
                             && (line.BinId is null || s.CurrentBinId == line.BinId)
                             && (line.LotId is null || s.LotId == line.LotId);
                    if (!ok) throw new ConflictException(PickBatchRules.SerialNotAvailable(number));
                    plan.Add(new PlannedPick(product, bin!.BinId, s!.LotId, s.SerialId, s.SerialNumber, 1m));
                }
                continue;
            }

            if (serials.Count > 0) { errors[$"lines[{i}].serialNumbers"] = new[] { PickBatchRules.SerialNotAllowed(product.Sku) }; continue; }

            var qty = line.Quantity!.Value;
            var candidates = remaining
                .Where(kv => kv.Key.ProductId == product.ProductId && bins.ContainsKey(kv.Key.BinId))
                .Select(kv =>
                {
                    var bin = bins[kv.Key.BinId];
                    return new PickCandidate(bin.BinId, bin.Code, bin.ZoneTypeCode, bin.Active, kv.Key.LotId, expiries[kv.Key], kv.Value);
                })
                .ToList();
            var allocation = PickBatchRules.Allocate(qty, candidates, line.BinId, line.LotId);
            if (!allocation.IsComplete)
            {
                var where = explicitBin?.Code ?? warehouse.Code;
                shortages[$"lines[{i}]"] = new[] { PickBatchRules.InsufficientStock(product.Sku, where, allocation.Allocated, qty) };
                continue;
            }
            foreach (var a in allocation.Allocations)
            {
                remaining[(product.ProductId, a.BinId, a.LotId)] -= a.Quantity;
                plan.Add(new PlannedPick(product, a.BinId, a.LotId, null, null, a.Quantity));
            }
        }

        if (errors.Count > 0) throw new ValidationException(errors);
        if (shortages.Count > 0) throw new PickShortageException(shortages.Values.First()[0]) { Errors = shortages };
        return plan;
    }

    // ================================================================ empacar (D11)

    public async Task<PickBatchPackResultDto> PackAsync(Guid publicId, PickBatchPackRequest req, CancellationToken ct)
    {
        // 2026-10-11: un despacho manual no se empaca (422 antes de pedir orders.create o los datos de la orden). Si no existe, el
        // orden de siempre (403 sin orders.create, luego 404) no cambia.
        var target = await db.Set<PickBatch>().AsNoTracking().Where(b => b.PublicId == publicId && b.IsActive)
            .Select(b => new { b.Number, b.ManualIssueReasonId }).FirstOrDefaultAsync(ct);
        if (target is not null && PickBatchRules.IsManual(target.ManualIssueReasonId))
            throw new StatusRuleException(PickBatchRules.ManualNotPackable(target.Number));
        await ValidatePackRequestAsync(req, ct);
        var orderDetail = await PackCoreAsync(publicId, req, ct);
        return new PickBatchPackResultDto(await GetAsync(publicId, ct), orderDetail);
    }

    /// <summary>Permiso orders.create y forma de la solicitud de empaque (400 sin orden o con entrega especial/chofer).</summary>
    private async Task ValidatePackRequestAsync(PickBatchPackRequest? req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.OrdersCreate, ct);
        if (req?.Order is null) throw new ValidationException("order", PickBatchRules.OrderRequired);
        if (!PickBatchRules.PackRequestAllowed(req.Order.IsSpecialDelivery, req.Order.DriverPublicId is not null))
            throw new ValidationException("order", PickBatchRules.PackSpecialNotAllowed);
    }

    private async Task<OrderDetailDto> PackCoreAsync(Guid publicId, PickBatchPackRequest req, CancellationToken ct)
    {
        var orderRequest = req.Order ?? throw new ValidationException("order", PickBatchRules.OrderRequired);
        var current = await ResolveAsync(publicId, ct);
        var orderDetail = await db.RunInTransactionAsync(async ct2 =>
        {
            var batch = await LockAsync(current.PickBatchId, ct2);
            if (!batch.IsActive) throw NotFound();
            if (PickBatchRules.IsManual(batch.ManualIssueReasonId)) throw new StatusRuleException(PickBatchRules.ManualNotPackable(batch.Number));
            var code = await StatusCodeOfAsync(batch.StatusCodeId, ct2);
            if (!PickBatchRules.CanPack(code, batch.IsActive)) throw new StatusRuleException(PickBatchRules.NotCollected(batch.Number));
            db.ApplyRowVersion(batch, req.RowVersion);

            // Dueño del inventario (D15): con cliente 3PL, la orden debe ser de ese cliente.
            var ownerId = await (from l in db.Set<PickBatchLine>().AsNoTracking()
                                 join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                                 where l.PickBatchId == batch.PickBatchId
                                 select p.ClientId).FirstOrDefaultAsync(ct2);
            if (ownerId is int ownerClientId)
            {
                var owner = await db.Clients.AsNoTracking().Where(c => c.ClientId == ownerClientId)
                    .Select(c => new { c.ClientId, c.PublicId, c.Name }).FirstAsync(ct2);
                if (orderRequest.ClientPublicId != owner.PublicId)
                    throw new ValidationException("order.clientPublicId", PickBatchRules.OrderClientMustBeOwner(owner.Name));
            }

            // La orden REAL, en esta transacción: número de empaque = número de la recolección, origen PICK_BATCH.
            var order = await orders.CreateAsync(orderRequest, OrderScope.Any,
                new OrderCreationOptions(batch.Number, EntityTypes.PickBatch, batch.PickBatchId), ct2);

            var packed = await statuses.TransitionAsync(StatusDomains.PickBatchStatus, EntityTypes.PickBatch, batch.PickBatchId,
                batch.StatusCodeId, PickBatchStatuses.Packed, null, ct2);
            batch.StatusCodeId = packed.StatusCodeId;
            batch.TransportOrderId = order.Id;
            batch.ClientInvoiceNumber = order.ClientInvoiceNumber; // factura final (tecleada o autogenerada, R40)
            batch.PackedAtUtc = DateTime.UtcNow;
            batch.PackedBy = tenant.UserId;
            await db.SaveGuardedAsync(PickBatchRules.OrderTaken, ct2); // UX_PickBatch_Order: última línea en BD
            return order;
        }, ct);

        return orderDetail;
    }

    // ================================================================ eliminar (D12/D13)

    public async Task DeleteAsync(Guid publicId, PickBatchDeleteRequest? req, CancellationToken ct)
    {
        var current = await ResolveAsync(publicId, ct);
        if (!current.IsActive) throw NotFound();
        // 2026-10-11: eliminar un despacho manual exige warehouse.issue (403 PERMISSION_DENIED), también desde /pick-batches.
        if (PickBatchRules.IsManual(current.ManualIssueReasonId))
            await permissions.EnsureAsync(PermissionCatalog.WarehouseIssue, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var batch = await LockAsync(current.PickBatchId, ct2);
            if (!batch.IsActive) throw NotFound(); // el segundo de dos DELETE simultáneos
            db.ApplyRowVersion(batch, req?.RowVersion);
            var code = await StatusCodeOfAsync(batch.StatusCodeId, ct2);

            if (string.Equals(code, PickBatchStatuses.Packed, StringComparison.OrdinalIgnoreCase))
            {
                await permissions.EnsureAsync(PermissionCatalog.OrdersCancel, ct2);
                var order = batch.TransportOrderId is int orderId
                    ? await db.TransportOrders.AsNoTracking().Where(o => o.TransportOrderId == orderId)
                        .Select(o => new { o.PublicId, o.IsActive, o.StatusCodeId }).FirstOrDefaultAsync(ct2)
                    : null;
                var orderStatus = order is null ? null
                    : await db.StatusCodes.AsNoTracking().FirstOrDefaultAsync(s => s.StatusCodeId == order.StatusCodeId, ct2);
                if (order is null || !PickBatchRules.CanDelete(code, order.IsActive, orderStatus?.IsInitial == true))
                {
                    var label = orderStatus is null ? "—" : MultilingualText.Resolve(orderStatus.LabelJson, tenant.Lang);
                    throw new StatusRuleException(PickBatchRules.DeleteBlocked(batch.Number, label));
                }
                // La orden se borra con ella (se une a esta transacción); desde Órdenes la guarda D12 lo impide.
                await orders.DeleteAsync(order.PublicId, OrderScope.Any, new OrderDeletionOptions(EntityTypes.PickBatch), ct2);
            }
            else if (!PickBatchRules.CanDelete(code, false, false))
            {
                throw new StatusRuleException(PickBatchRules.AlreadyCancelled(batch.Number));
            }

            // Reversa por línea (D13): ADJUSTMENT de entrada PICK_BATCH_REVERSAL a la posición original, Ref PICK_BATCH.
            var lines = await db.Set<PickBatchLine>().AsTracking()
                .Where(l => l.PickBatchId == batch.PickBatchId && l.ReversalTxnId == null)
                .OrderBy(l => l.PickBatchLineId).ToListAsync(ct2);
            if (lines.Count > 0)
            {
                var bins = await BinsOfWarehouseAsync(batch.WarehouseId, ct2);
                foreach (var l in lines)
                {
                    var bin = bins.GetValueOrDefault(l.FromBinId);
                    if (bin is null || !bin.Active)
                        throw new StatusRuleException(PickBatchRules.ReversalBinInactive(bin?.Code ?? l.FromBinId.ToString()));
                }
                var serialIds = lines.Where(l => l.SerialId != null).Select(l => l.SerialId!.Value).ToList();
                var serialNumbers = serialIds.Count == 0
                    ? new Dictionary<int, string>()
                    : await (from s in db.Set<InventorySerial>().AsNoTracking()
                             join p in db.Set<Product>().AsNoTracking() on s.ProductId equals p.ProductId
                             where serialIds.Contains(s.SerialId)
                             select new { s.SerialId, s.SerialNumber }).ToDictionaryAsync(x => x.SerialId, x => x.SerialNumber, ct2);

                var postings = lines.Select(l => new InventoryPosting(InventoryTxnTypes.Adjustment, l.ProductId, l.Quantity,
                        LotId: l.LotId, SerialId: l.SerialId,
                        SerialNumber: l.SerialId is int sid ? serialNumbers.GetValueOrDefault(sid) : null,
                        ToWarehouseId: batch.WarehouseId, ToBinId: l.FromBinId,
                        RefEntityType: EntityTypes.PickBatch, RefId: batch.PickBatchId,
                        ReasonCode: AdjustmentReasons.PickBatchReversal))
                    .ToList();
                var txnIds = await ledger.PostAsync(postings, ct2);
                for (var i = 0; i < lines.Count; i++) lines[i].ReversalTxnId = txnIds[i];
            }

            var cancelled = await statuses.TransitionAsync(StatusDomains.PickBatchStatus, EntityTypes.PickBatch, batch.PickBatchId,
                batch.StatusCodeId, PickBatchStatuses.Cancelled, string.IsNullOrWhiteSpace(req?.Comment) ? null : req!.Comment!.Trim(), ct2);
            batch.StatusCodeId = cancelled.StatusCodeId;
            batch.CancelledAtUtc = DateTime.UtcNow;
            batch.CancelledBy = tenant.UserId;
            batch.IsActive = false;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
    }

    // ================================================================ armado de DTOs (sin N+1)

    private async Task<Dictionary<int, PickBatchDto>> BuildDtosAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var result = new Dictionary<int, PickBatchDto>();
        if (ids.Count == 0) return result;
        var idList = ids.ToList();

        var batches = await db.Set<PickBatch>().AsNoTracking().Where(b => idList.Contains(b.PickBatchId)).ToListAsync(ct);
        var whIds = batches.Select(b => b.WarehouseId).Distinct().ToList();
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.WarehouseId))
            .Select(w => new { w.WarehouseId, w.PublicId, w.Code }).ToDictionaryAsync(w => w.WarehouseId, ct);

        var lines = await (from l in db.Set<PickBatchLine>().AsNoTracking()
                           join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                           join lot in db.Set<InventoryLot>().AsNoTracking() on l.LotId equals (int?)lot.LotId into lj
                           from lot in lj.DefaultIfEmpty()
                           join s in db.Set<InventorySerial>().AsNoTracking() on l.SerialId equals (int?)s.SerialId into sj
                           from s in sj.DefaultIfEmpty()
                           where idList.Contains(l.PickBatchId)
                           orderby l.PickBatchLineId
                           select new
                           {
                               Line = l, p.PublicId, p.Sku, p.Name, OwnerClientId = p.ClientId,
                               LotNumber = lot == null ? null : lot.LotNumber,
                               SerialNumber = s == null ? null : s.SerialNumber,
                           }).ToListAsync(ct);
        var binIds = lines.Select(x => x.Line.FromBinId).Distinct().ToList();
        var binCodes = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                              join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                              where binIds.Contains(b.WarehouseBinId)
                              select new { b.WarehouseBinId, b.Code }).ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code, ct);

        var orderIds = batches.Where(b => b.TransportOrderId != null).Select(b => b.TransportOrderId!.Value).Distinct().ToList();
        var orderRows = await (from o in db.TransportOrders.AsNoTracking()
                               join c in db.Clients.AsNoTracking() on o.ClientId equals c.ClientId
                               where orderIds.Contains(o.TransportOrderId)
                               select new
                               {
                                   o.TransportOrderId, o.PublicId, o.OrderNumber, o.PackBatchNumber, o.ClientInvoiceNumber,
                                   o.StatusCodeId, o.IsActive, ClientName = c.Name,
                               }).ToDictionaryAsync(o => o.TransportOrderId, ct);

        var statusIds = batches.Select(b => b.StatusCodeId).Concat(orderRows.Values.Select(o => o.StatusCodeId)).Distinct().ToList();
        var statusMap = await db.StatusCodes.AsNoTracking().Where(s => statusIds.Contains(s.StatusCodeId))
            .ToDictionaryAsync(s => s.StatusCodeId, ct);

        var userIds = batches.Where(b => b.CollectedBy != null).Select(b => b.CollectedBy!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName }).ToDictionaryAsync(u => u.Id, ct);

        // 2026-10-11: motivo del despacho manual y cliente dueño del inventario (un solo dueño por documento, D15).
        var reasons = await ReasonsAsync(batches.Where(b => b.ManualIssueReasonId != null).Select(b => b.ManualIssueReasonId!.Value), ct);
        var ownerIds = lines.Where(x => x.OwnerClientId != null).Select(x => x.OwnerClientId!.Value).Distinct().ToList();
        var owners = ownerIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Clients.AsNoTracking().Where(c => ownerIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);

        var lang = tenant.Lang;
        var linesByBatch = lines.GroupBy(x => x.Line.PickBatchId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var b in batches)
        {
            var status = statusMap.GetValueOrDefault(b.StatusCodeId);
            var statusCode = status?.InternalCode ?? string.Empty;
            var wh = warehouses.GetValueOrDefault(b.WarehouseId);
            var order = b.TransportOrderId is int oid ? orderRows.GetValueOrDefault(oid) : null;
            var orderStatus = order is null ? null : statusMap.GetValueOrDefault(order.StatusCodeId);
            var own = linesByBatch.GetValueOrDefault(b.PickBatchId) ?? new();
            var invoice = b.ClientInvoiceNumber ?? order?.ClientInvoiceNumber;
            var collectedBy = b.CollectedBy is int uid && users.TryGetValue(uid, out var u)
                ? (string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName)
                : null;

            var canDelete = b.IsActive && PickBatchRules.CanDelete(statusCode, order?.IsActive == true, orderStatus?.IsInitial == true);
            var isManual = PickBatchRules.IsManual(b.ManualIssueReasonId);
            var reason = b.ManualIssueReasonId is int mrid && reasons.TryGetValue(mrid, out var rr) ? rr : ((string Code, string Label)?)null;
            var ownerName = own.Select(x => x.OwnerClientId).FirstOrDefault(x => x != null) is int ownerId ? owners.GetValueOrDefault(ownerId) : null;
            var lineDtos = own.Select(x => new PickBatchLineDto(x.Line.PickBatchLineId, x.PublicId, x.Sku, x.Name, x.Line.Quantity,
                    x.Line.FromBinId, binCodes.GetValueOrDefault(x.Line.FromBinId) ?? string.Empty, x.Line.LotId, x.LotNumber, x.SerialNumber,
                    x.Line.UnitCost, x.Line.IssueTxnId, x.Line.ReversalTxnId))
                .ToList();

            result[b.PickBatchId] = new PickBatchDto(
                b.PickBatchId, b.PublicId, b.Number,
                wh?.PublicId ?? Guid.Empty, wh?.Code ?? string.Empty,
                statusCode, status is null ? statusCode : MultilingualText.Resolve(status.LabelJson, lang),
                b.CollectedAtUtc, collectedBy, b.PackedAtUtc,
                order?.PublicId, order?.OrderNumber, order?.PackBatchNumber, invoice,
                orderStatus?.InternalCode, orderStatus is null ? null : MultilingualText.Resolve(orderStatus.LabelJson, lang),
                order?.ClientName,
                order is null ? null : PickBatchRules.DisplayNumbers(order.OrderNumber, invoice),
                PickBatchRules.CanPack(statusCode, b.IsActive, isManual), canDelete,
                own.Sum(x => x.Line.Quantity),
                PickBatchRules.TotalCost(own.Select(x => (x.Line.Quantity, x.Line.UnitCost))),
                lineDtos, b.IsActive,
                Convert.ToBase64String(b.RowVersion ?? Array.Empty<byte>()),
                isManual, reason?.Code, reason?.Label, b.Note, ownerName);
        }
        return result;
    }

    // ================================================================ resolución (bajo el filtro de tenant)

    private async Task<PickBatch> ResolveAsync(Guid publicId, CancellationToken ct)
        => await db.Set<PickBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.PublicId == publicId, ct) ?? throw NotFound();

    private static NotFoundException NotFound() => new(PickBatchLabel, null, true);

    /// <summary>El almacén indicado o, si se omite, el único activo del tenant (D26): ninguno o más de uno → 400.</summary>
    private Task<Warehouse> ResolveWarehouseOrDefaultAsync(Guid? publicId, CancellationToken ct)
        => WmsResolve.ResolveWarehouseOrDefaultAsync(db, publicId, ct); // única implementación (D26): 404 / 400 con más de uno / 422 sin ninguno

    /// <summary>Productos del tenant por PublicId (404 'Producto no encontrado.' si alguno no es del tenant), con su seguimiento.</summary>
    private async Task<Dictionary<Guid, ProductInfo>> ProductsAsync(IEnumerable<Guid> publicIds, CancellationToken ct)
    {
        var ids = publicIds.Distinct().ToList();
        var rows = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.PublicId))
            .Select(p => new { p.ProductId, p.PublicId, p.Sku, p.IsActive, p.ClientId, p.TrackingTypeLookupId, p.PurchaseCost })
            .ToListAsync(ct);
        if (rows.Count != ids.Count) throw new NotFoundException("Producto");
        var result = new Dictionary<Guid, ProductInfo>();
        foreach (var p in rows)
        {
            var tracking = (await lookups.GetAsync(p.TrackingTypeLookupId, ct))?.InternalCode ?? TrackingTypes.None;
            result[p.PublicId] = new ProductInfo(p.ProductId, p.PublicId, p.Sku, p.IsActive, p.ClientId, tracking, p.PurchaseCost);
        }
        return result;
    }

    /// <summary>Posiciones del almacén (filtrado por tenant) con su tipo de zona; activa = posición y zona activas.</summary>
    private async Task<Dictionary<int, BinInfo>> BinsOfWarehouseAsync(int warehouseId, CancellationToken ct)
    {
        var rows = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                          join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                          join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                          where b.WarehouseId == warehouseId
                          select new { b.WarehouseBinId, b.Code, BinActive = b.IsActive, ZoneActive = z.IsActive, z.ZoneTypeLookupId })
            .ToListAsync(ct);
        var result = new Dictionary<int, BinInfo>();
        foreach (var r in rows)
        {
            var zoneType = r.ZoneTypeLookupId is int zt ? (await lookups.GetAsync(zt, ct))?.InternalCode : null;
            result[r.WarehouseBinId] = new BinInfo(r.WarehouseBinId, r.Code, r.BinActive && r.ZoneActive, zoneType);
        }
        return result;
    }

    /// <summary>Lote del producto (se alcanza por su producto filtrado) o 404 'Lote no encontrado.'.</summary>
    private async Task<InventoryLot> ResolveLotAsync(int productId, int lotId, CancellationToken ct)
        => await (from l in db.Set<InventoryLot>().AsNoTracking()
                  join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                  where l.LotId == lotId && l.ProductId == productId
                  select l).FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException("Lote");

    /// <summary>Series del producto por número (sin distinguir mayúsculas), alcanzadas por su producto filtrado.</summary>
    private async Task<Dictionary<string, SerialRow>> SerialsAsync(int productId, IReadOnlyList<string> serials, CancellationToken ct)
    {
        var wanted = serials.ToList();
        var rows = await (from s in db.Set<InventorySerial>().AsNoTracking()
                          join p in db.Set<Product>().AsNoTracking() on s.ProductId equals p.ProductId
                          where s.ProductId == productId && wanted.Contains(s.SerialNumber)
                          select new SerialRow(s.SerialId, s.SerialNumber, s.LotId, s.StatusCodeId, s.CurrentWarehouseId, s.CurrentBinId))
            .ToListAsync(ct);
        var result = new Dictionary<string, SerialRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows) result[r.SerialNumber] = r;
        return result;
    }

    private async Task<string?> StatusCodeOfAsync(int statusCodeId, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct);

    // ================================================================ adaptador a la costura de P0 (InventoryQueries)
    // Toda dependencia de las firmas de InventoryQueries de esta pieza vive aquí (mismo patrón que P1, P2, P4, P5 y P6).

    /// <summary>Encabezado PickBatch con UPDLOCK, tracked (primero en el orden de encabezados del lote).</summary>
    private Task<PickBatch> LockAsync(int pickBatchId, CancellationToken ct) => db.LockPickBatchAsync(pickBatchId, ct);

    /// <summary>
    /// Faltante detectado al asignar (antes de bloquear): mismo código y forma que InventoryLedger (409 insufficient_stock con
    /// errores por línea). El ledger vuelve a verificar con los saldos bloqueados.
    /// </summary>
    private sealed class PickShortageException(string message) : TeikemException(message, 409, "insufficient_stock");
}
