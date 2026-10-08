using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
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
/// Lote 13 — ciclo de estatus: EXPECTED (solo encabezado) → RECEIVING ↔ DISCREPANCY (lo sincroniza SyncOpenStatusAsync con
/// cada cambio de líneas, siempre por StatusService) → RECEIVED o RECEIVED_VARIANCE al confirmar → PUTAWAY. Encabezado
/// editable (UpdateHeaderAsync), esperado capturable en ciegos y devoluciones (la diferencia solo marca el estatus: en el
/// Kárdex entra lo recibido), filtros phase/variance.
/// Lote 6 (P4) — Recepción (R7, R8, R9, R10 para crear el putaway, R18). Un recibo nace abierto con número REC-#####:
/// - contra un ASN de cliente (EXPECTED, mismo almacén, bloqueado): una línea por línea del aviso;
/// - contra una PO (purchasing.receive + módulo PURCHASING): la costura IPurchaseOrderReceiving (P8) bloquea y valida la
///   PO y devuelve lo pendiente; se crea un ASN por ese pendiente y el recibo sobre él (una PO admite varias entregas, D6);
/// - ciego (BLIND) o de devolución (RETURN) con las líneas de la solicitud.
/// La cantidad recibida arranca igual a la esperada (R8). Confirmar (D4, D5) asienta en el ledger RECEIPT por lo esperado y
/// ADJUSTMENT RECEIPT_VARIANCE por la diferencia (ReceiptPostingRules), enlaza AdjustmentTxnId, aplica la PO, pasa el ASN a
/// RECEIVED, reparte el cruce de muelle (IReceiptConfirmationParticipant, P9) y crea PUTAWAY por el remanente con posición
/// sugerida; sin ninguna PUTAWAY el recibo pasa directo a PUTAWAY.
///
/// Orden de bloqueo (InventoryQueries): ReceiptHeader → Asn → PurchaseOrder → saldos (ledger) → series → NumberSequence.
/// La creación no bloquea recibo (aún no existe): Asn o PO, y el número REC al final. El contenido de un recibo confirmado
/// queda congelado (ya está en el ledger): toda edición re-verifica que siga abierto con el encabezado bloqueado.
/// El TenantId sale del principal; el recibo se expone por PublicId y sus líneas (sin TenantId) por id int SIEMPRE dentro
/// de su recibo (otra línea → 404, sin oráculo).
/// </summary>
public sealed class ReceiptService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    INumberSequenceService numbers,
    PermissionService permissions,
    ModuleService modules,
    AsnService asns,
    InventoryLedger ledger,
    WarehouseTaskWriter taskWriter,
    PutawaySuggester suggester,
    IPurchaseOrderReceiving purchaseOrders,
    IEnumerable<IReceiptConfirmationParticipant> participants,
    IEnumerable<IWarehouseTaskHandler> taskHandlers,
    DamageService damages)
{
    public const string NumberPattern = "REC-#####";
    public const string NumberTakenMessage = "Ya existe un recibo con ese número; intente de nuevo.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ================================================================ lista

    public async Task<ReceiptPageDto> ListAsync(ReceiptQuery q, CancellationToken ct)
    {
        q ??= new ReceiptQuery();
        var skip = Math.Max(0, q.Skip);
        var take = q.Take <= 0 ? 100 : Math.Min(q.Take, ReceiptRules.MaxPageSize);
        var asnTypeId = await lookups.GetIdAsync(LookupDomains.ReceiptType, ReceiptTypes.Asn, ct);

        var query = db.Set<ReceiptHeader>().AsNoTracking().Where(r => r.IsActive);
        if (q.WarehousePublicId is Guid wh)
            query = query.Where(r => db.Set<Warehouse>().Any(w => w.WarehouseId == r.WarehouseId && w.PublicId == wh));
        if (q.Status is { Length: > 0 })
        {
            var codes = Codes(q.Status);
            var ids = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.ReceiptStatus && codes.Contains(s.InternalCode))
                .Select(s => s.StatusCodeId).ToListAsync(ct);
            query = query.Where(r => ids.Contains(r.StatusCodeId));
        }
        if (q.Types is { Length: > 0 })
        {
            var codes = Codes(q.Types);
            var ids = await db.LookupCodes.AsNoTracking().Where(l => l.Entity == LookupDomains.ReceiptType && codes.Contains(l.InternalCode))
                .Select(l => l.LookupCodeId).ToListAsync(ct);
            query = query.Where(r => ids.Contains(r.ReceiptTypeLookupId));
        }
        // Fechas en UTC sobre la fecha de alta: 'hasta' inclusive (exclusivo +1 día).
        if (q.From is DateOnly from)
        {
            var f = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(r => r.CreatedAtUtc >= f);
        }
        if (q.To is DateOnly to)
        {
            var t = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(r => r.CreatedAtUtc < t);
        }
        if (q.ProductPublicIds is { Length: > 0 })
        {
            var pids = q.ProductPublicIds.Distinct().ToList();
            query = query.Where(r => (from l in db.Set<ReceiptLine>()
                                      join p in db.Set<Product>() on l.ProductId equals p.ProductId
                                      where l.ReceiptHeaderId == r.ReceiptHeaderId && pids.Contains(p.PublicId)
                                      select l.ReceiptLineId).Any());
        }
        if (q.HasVariance is bool hv)
        {
            // Misma regla que ReceiptRules.HasVariance, traducible a SQL.
            query = hv
                ? query.Where(r => db.Set<ReceiptLine>().Any(l => l.ReceiptHeaderId == r.ReceiptHeaderId
                    && ((l.ExpectedQty != null && l.ReceivedQty != l.ExpectedQty) || (l.ExpectedQty == null && r.ReceiptTypeLookupId == asnTypeId && l.ReceivedQty != 0))))
                : query.Where(r => !db.Set<ReceiptLine>().Any(l => l.ReceiptHeaderId == r.ReceiptHeaderId
                    && ((l.ExpectedQty != null && l.ReceivedQty != l.ExpectedQty) || (l.ExpectedQty == null && r.ReceiptTypeLookupId == asnTypeId && l.ReceivedQty != 0))));
        }
        // Lote 13: fase (OPEN = EXPECTED/RECEIVING/DISCREPANCY; PENDING_PUTAWAY = confirmados; DONE = PUTAWAY).
        if (!string.IsNullOrWhiteSpace(q.Phase))
        {
            var (phaseCodes, phaseError) = ReceiptStatusRules.PhaseCodes(q.Phase);
            if (phaseError is not null) throw new ValidationException("phase", phaseError);
            var phaseIds = await db.StatusIdsAsync(StatusDomains.ReceiptStatus, phaseCodes, ct);
            query = query.Where(r => phaseIds.Contains(r.StatusCodeId));
        }
        // Lote 13: diferencia SHORT (alguna línea recibió menos de lo esperado), OVER (alguna recibió más, o una línea extra
        // de un recibo ASN recibió algo) o NONE (ninguna línea con diferencia, la negación de hasVariance); varias con O.
        if (q.Variance is { Length: > 0 })
        {
            var (variance, varianceError) = ReceiptStatusRules.ParseVariance(q.Variance);
            if (varianceError is not null) throw new ValidationException("variance", varianceError);
            if (variance.Count > 0)
            {
                var wantShort = variance.Contains(ReceiptStatusRules.VarianceShort);
                var wantOver = variance.Contains(ReceiptStatusRules.VarianceOver);
                var wantNone = variance.Contains(ReceiptStatusRules.VarianceNone);
                query = query.Where(r =>
                    (wantShort && db.Set<ReceiptLine>().Any(l => l.ReceiptHeaderId == r.ReceiptHeaderId
                        && l.ExpectedQty != null && l.ReceivedQty < l.ExpectedQty))
                    || (wantOver && db.Set<ReceiptLine>().Any(l => l.ReceiptHeaderId == r.ReceiptHeaderId
                        && ((l.ExpectedQty != null && l.ReceivedQty > l.ExpectedQty) || (l.ExpectedQty == null && r.ReceiptTypeLookupId == asnTypeId && l.ReceivedQty > 0))))
                    || (wantNone && !db.Set<ReceiptLine>().Any(l => l.ReceiptHeaderId == r.ReceiptHeaderId
                        && ((l.ExpectedQty != null && l.ReceivedQty != l.ExpectedQty) || (l.ExpectedQty == null && r.ReceiptTypeLookupId == asnTypeId && l.ReceivedQty != 0)))));
            }
        }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(r => r.Number.Contains(s)
                || (r.Reference != null && r.Reference.Contains(s)) || (r.Carrier != null && r.Carrier.Contains(s))   // Lote 13
                || db.Set<Asn>().Any(a => a.AsnId == r.AsnId
                    && ((a.Reference != null && a.Reference.Contains(s))
                        || db.Set<Client>().Any(c => c.ClientId == a.ClientId && c.Name.Contains(s))
                        || db.Set<PurchaseOrder>().Any(p => p.PurchaseOrderId == a.PurchaseOrderId
                            && (p.Number.Contains(s) || db.Set<Supplier>().Any(su => su.SupplierId == p.SupplierId && su.Name.Contains(s)))))));
        }

        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.ReceiptHeaderId)
            .Skip(skip).Take(take).ToListAsync(ct);
        var items = await HeadersAsync(page, ct);
        if (!q.IncludeLines)
            return new ReceiptPageDto(total, skip, take, page.Select(r => items[r.ReceiptHeaderId]).ToList());

        // Exportación con líneas: las de TODOS los recibos de la página en un solo lote de consultas (sin N+1).
        var lines = await LinesAsync(page, items, ct);
        return new ReceiptPageDto(total, skip, take, page.Select(r => items[r.ReceiptHeaderId] with
        {
            Lines = lines.ByReceipt.GetValueOrDefault(r.ReceiptHeaderId) ?? (IReadOnlyList<ReceiptLineDto>)Array.Empty<ReceiptLineDto>(),
        }).ToList());
    }

    // ================================================================ ficha

    public async Task<ReceiptDetailDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var r = await db.Set<ReceiptHeader>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId && x.IsActive, ct)
                ?? throw new NotFoundException("Recibo");
        var header = (await HeadersAsync(new List<ReceiptHeader> { r }, ct))[r.ReceiptHeaderId];
        var lines = await LinesAsync(new List<ReceiptHeader> { r }, new Dictionary<int, ReceiptListItemDto> { [r.ReceiptHeaderId] = header }, ct);
        var lineDtos = lines.ByReceipt.GetValueOrDefault(r.ReceiptHeaderId) ?? new List<ReceiptLineDto>();

        var tasks = await PutawayTasksAsync(r, header, ct);
        // Lote 13: se puede borrar mientras está abierto y sin asignaciones de cruce de muelle (mismas reglas que DeleteAsync).
        var canDelete = header.IsOpen && lines.CrossDock.Count == 0;
        return new ReceiptDetailDto(header, lineDtos, tasks, Convert.ToBase64String(r.RowVersion ?? Array.Empty<byte>()), canDelete);
    }

    // ================================================================ alta

    /// <summary>
    /// Alta abierta (flujo por pasos; EXPECTED sin líneas, RECEIVING o DISCREPANCY con líneas) o, con Confirm = true (Lote 8A, cola del aparato), alta + captura de las líneas de la
    /// solicitud + confirmación en UNA transacción: se reutilizan exactamente los mismos pasos (y mensajes) que el alta y la
    /// confirmación por separado; si cualquiera falla no queda nada (ni el número REC se consume). Contra aviso u orden de
    /// compra, las Lines de la solicitud se aplican sobre las líneas del documento (ApplyRequestLinesToAsnAsync); sin Lines
    /// se recibe lo esperado (R8).
    /// </summary>
    public async Task<ReceiptDetailDto> CreateAsync(ReceiptCreateRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (!req.Confirm) return await GetAsync(await CreateCoreAsync(req, ct), ct);

        // Permisos del recibo contra PO ANTES de la transacción externa: un PERMISSION_DENIED escrito dentro se revertiría
        // con ella (la verificación anidada vuelve a pasar sin escribir nada).
        if (req.PurchaseOrderPublicId is not null && req.AsnId is null)
        {
            await permissions.EnsureAsync(PermissionCatalog.PurchasingReceive, ct);
            await modules.EnsureEnabledAsync(ModuleKeys.Purchasing, ct);
        }
        // Fila del contador REC en autocommit ANTES de abrir la transacción externa (patrón de NumberSequenceService);
        // la llamada anidada de CreateCoreAsync la encuentra y no inserta nada.
        await numbers.EnsureAsync(NumberKinds.Receipt, null, ct);
        var publicId = await db.RunInTransactionAsync(async ct2 =>
        {
            // Las transacciones internas de alta y confirmación se unen a ésta (RunInTransactionAsync anidado).
            var created = await CreateCoreAsync(req, ct2);
            await ConfirmCoreAsync(created, null, ct2);
            return created;
        }, ct);
        return await GetAsync(publicId, ct);
    }

    private async Task<Guid> CreateCoreAsync(ReceiptCreateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        if (req.AsnId is not null && req.PurchaseOrderPublicId is not null)
            throw new ValidationException("asnId", ReceiptRules.AsnOrPurchaseOrder);

        var typeCode = ReceiptTypes.Asn;
        if (req.AsnId is null && req.PurchaseOrderPublicId is null)
        {
            var (t, typeError) = ReceiptRules.ParseManualType(req.Type);
            if (typeError is not null) throw new ValidationException("type", typeError);
            typeCode = t!;
        }
        else if (!string.IsNullOrWhiteSpace(req.Type) && !string.Equals(req.Type.Trim(), ReceiptTypes.Asn, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("type", ReceiptRules.UnknownType(req.Type.Trim()));
        }
        var typeLookupId = await lookups.GetIdAsync(LookupDomains.ReceiptType, typeCode, ct);

        // Almacén: el del aviso/PO; si la solicitud trae uno distinto → 400. Ciego/devolución: el indicado o el único activo.
        Warehouse? requested = req.WarehousePublicId is Guid wpid ? await ReceivingSupport.ResolveWarehouseOrDefaultAsync(db, wpid, ct) : null;

        if (req.PurchaseOrderPublicId is not null)
        {
            // Recibir contra PO exige además purchasing.receive y el módulo PURCHASING (el controlador ya exigió warehouse.receive).
            await permissions.EnsureAsync(PermissionCatalog.PurchasingReceive, ct);
            await modules.EnsureEnabledAsync(ModuleKeys.Purchasing, ct);
        }

        // Lote 13: transporte y referencia del encabezado (texto libre, máximo 80; vacío = sin dato).
        var textErrors = new Dictionary<string, string[]>();
        var (carrier, carrierError) = ReceiptRules.CreateText(req.Carrier, ReceiptRules.CarrierTooLong);
        if (carrierError is not null) textErrors["carrier"] = new[] { carrierError };
        var (reference, referenceError) = ReceiptRules.CreateText(req.Reference, ReceiptRules.ReferenceTooLong);
        if (referenceError is not null) textErrors["reference"] = new[] { referenceError };
        // Lote 16: modo del recibo (sin él, el del almacén; D9 lo resuelve CreateMode dentro de la transacción).
        var (requestedMode, modeError) = ReceivingModeRules.ParseMode(req.ReceivingMode);
        if (modeError is not null) textErrors["receivingMode"] = new[] { modeError };
        if (textErrors.Count > 0) throw new ValidationException(textErrors);

        // Ciego/devolución: almacén indicado o el único activo; el tope de líneas se valida antes de abrir la transacción.
        // Lote 13: sin líneas se crea solo el encabezado (nace EXPECTED); las líneas son obligatorias solo con Confirm.
        Warehouse? manualWarehouse = null;
        var requestLines = req.Lines ?? Array.Empty<ReceiptLineRequest>();
        var anyTarget = requestLines.Any(l => l is not null && (l.TargetBinId is not null || !string.IsNullOrWhiteSpace(l.TargetBinCode)));
        if (typeCode != ReceiptTypes.Asn)
        {
            manualWarehouse = requested ?? await ReceivingSupport.ResolveWarehouseOrDefaultAsync(db, null, ct);
            if (!manualWarehouse.IsActive) throw new StatusRuleException(ReceiptRules.WarehouseInactive);
            if (requestLines.Count == 0 && req.Confirm) throw new ValidationException("lines", ReceiptRules.LinesRequired);
            if (requestLines.Count > ReceiptRules.MaxLines) throw new ValidationException("lines", ReceiptRules.TooManyLines);
        }

        await numbers.EnsureAsync(NumberKinds.Receipt, null, ct);
        var initial = await statuses.GetInitialAsync(StatusDomains.ReceiptStatus, ct);
        var expectedAsnId = await db.StatusIdAsync(StatusDomains.AsnStatus, AsnStatuses.Expected, ct);
        var openReceiptIds = await db.StatusIdsAsync(StatusDomains.ReceiptStatus, ReceiptStatuses.OpenCodes, ct);

        var publicId = await db.RunInTransactionAsync(async ct2 =>
        {
            Warehouse warehouse;
            Asn? asn = null;
            List<ReceiptLine> lines;

            if (typeCode == ReceiptTypes.Asn)
            {
                if (req.PurchaseOrderPublicId is Guid poPublicId)
                {
                    // P8 bloquea la PO y valida IsReceivable/NothingPending; aquí el ASN nace por lo pendiente.
                    var po = await purchaseOrders.LockForReceiptAsync(poPublicId, ct2);
                    if (requested is not null && requested.WarehouseId != po.WarehouseId)
                        throw new ValidationException("warehousePublicId", ReceiptRules.PurchaseOrderOtherWarehouse);
                    // Bajo el bloqueo de la PO: un segundo recibo abierto (EXPECTED, RECEIVING o DISCREPANCY) contaría dos
                    // veces el mismo pendiente → 409.
                    var poHasOpen = await (from r in db.Set<ReceiptHeader>().AsNoTracking()
                                           join a in db.Set<Asn>().AsNoTracking() on r.AsnId equals a.AsnId
                                           where a.PurchaseOrderId == po.PurchaseOrderId && r.IsActive && openReceiptIds.Contains(r.StatusCodeId)
                                           select r.ReceiptHeaderId).AnyAsync(ct2);
                    if (poHasOpen) throw new ConflictException(ReceiptRules.PurchaseOrderHasOpenReceipt);
                    asn = await asns.CreateForPurchaseOrderAsync(po, ct2);
                }
                else
                {
                    var asnId = req.AsnId!.Value;
                    // El ASN se alcanza por su filtro de tenant antes de bloquearlo (otro tenant → 404, sin oráculo).
                    if (!await db.Set<Asn>().AsNoTracking().AnyAsync(a => a.AsnId == asnId && a.IsActive, ct2))
                        throw new NotFoundException(AsnService.AsnWhat);
                    asn = await ReceivingSupport.LockAsnAsync(db, asnId, ct2);
                    if (asn.StatusCodeId != expectedAsnId) throw new StatusRuleException(ReceiptRules.AsnNotExpected);
                    if (requested is not null && requested.WarehouseId != asn.WarehouseId)
                        throw new ValidationException("warehousePublicId", ReceiptRules.AsnOtherWarehouse);
                    if (await db.Set<ReceiptHeader>().AsNoTracking().AnyAsync(r => r.AsnId == asn.AsnId && r.IsActive, ct2))
                        throw new ConflictException(ReceiptRules.AsnBusy);
                }
                warehouse = await db.Set<Warehouse>().AsNoTracking().FirstAsync(w => w.WarehouseId == asn.WarehouseId, ct2);
                if (!warehouse.IsActive) throw new StatusRuleException(ReceiptRules.WarehouseInactive);
                lines = await LinesFromAsnAsync(asn, ct2);
                // Lote 8A: con Lines en la solicitud (cola del aparato) lo escaneado manda sobre lo esperado; nunca se ignoran.
                if (requestLines.Count > 0) await ApplyRequestLinesToAsnAsync(warehouse.WarehouseId, asn, lines, requestLines, ct2);
            }
            else
            {
                // Líneas de la solicitud, construidas DENTRO del delegado (el reintento limpia el ChangeTracker; el lote
                // nuevo se crea en la misma transacción y se revierte con ella).
                warehouse = manualWarehouse!;
                lines = new List<ReceiptLine>();
                var errors = new Dictionary<string, string[]>();
                for (var i = 0; i < requestLines.Count; i++)
                {
                    var built = await BuildLineAsync(warehouse.WarehouseId, requestLines[i], $"lines[{i}]", OwnerRule.Any, null, true, errors, ct2);
                    if (built is not null) lines.Add(built);
                }
                if (errors.Count > 0) throw new ValidationException(errors);
            }

            // Lote 16: modo con que nace el recibo (copia: cambiar el del almacén después no lo toca, D2).
            var mode = ReceivingModeRules.CreateMode(requestedMode, await LookupCodeOfAsync(warehouse.ReceivingModeLookupId, ct2), req.Confirm, anyTarget);
            var modeId = await ModeIdAsync(mode, ct2);

            // Posición de recepción por defecto (la de la solicitud o la del almacén) para las líneas que no la traen. Lote 13:
            // la de la solicitud queda además en el encabezado (DefaultStagingBinId); sin ella se sigue exigiendo que el almacén
            // tenga una zona STAGING (422 NoStagingBin), aunque el recibo nazca sin líneas. Lote 16: en un recibo directo no se
            // exige (la mercancía entra a su posición destino); si existe, queda para las líneas con cruce de muelle (D11).
            int? headerStagingId = req.StagingBinId is int sb
                ? (await ReceivingSupport.ResolveStagingBinAsync(db, warehouse.WarehouseId, sb, "stagingBinId", ct2)).BinId
                : null;
            var stagingId = headerStagingId ?? (ReceivingModeRules.IsDirect(mode)
                ? await ReceivingSupport.TryDefaultStagingBinAsync(db, warehouse.WarehouseId, ct2)
                : await ReceivingSupport.DefaultStagingBinAsync(db, warehouse.WarehouseId, ct2));
            foreach (var l in lines) l.StagingBinId ??= stagingId;

            if (req.DockId is int dockId) await EnsureDockAsync(warehouse.WarehouseId, dockId, ct2);

            // Número REC al final (último bloqueo del orden del lote).
            var seq = await numbers.NextAsync(NumberKinds.Receipt, null, ct2);
            var receipt = new ReceiptHeader
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = warehouse.WarehouseId, AsnId = asn?.AsnId, DockId = req.DockId,
                DefaultStagingBinId = headerStagingId, Carrier = carrier, Reference = reference, ReceivingModeLookupId = modeId,
                ReceiptTypeLookupId = typeLookupId, Number = NumberFormat.Resolve(NumberPattern, seq),
                StatusCodeId = initial.StatusCodeId, IsActive = true, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
            };
            db.Set<ReceiptHeader>().Add(receipt);
            await SaveReceiptAsync(ct2);
            foreach (var l in lines) l.ReceiptHeaderId = receipt.ReceiptHeaderId;
            db.Set<ReceiptLine>().AddRange(lines);
            var born = await statuses.TransitionAsync(StatusDomains.ReceiptStatus, EntityTypes.Receipt, receipt.ReceiptHeaderId, null, initial.InternalCode, null, ct2);
            receipt.StatusCodeId = born.StatusCodeId;
            await SaveReceiptAsync(ct2);
            // Lote 13: con líneas pasa a RECEIVING (o DISCREPANCY si lo recibido difiere de lo esperado); sin líneas se queda EXPECTED.
            await SyncOpenStatusAsync(receipt, ct2);
            await SaveReceiptAsync(ct2);
            return receipt.PublicId;
        }, ct);

        return publicId;
    }

    // ================================================================ encabezado (Lote 13)

    /// <summary>
    /// Lote 13 — PATCH del encabezado de un recibo abierto (EXPECTED, RECEIVING o DISCREPANCY; confirmado → 422), con el
    /// encabezado bloqueado y rowVersion (409). null = no cambiar:
    /// - Type BLIND ↔ RETURN solo sin aviso ni orden de compra (400 TypeFixedWithDocument);
    /// - almacén solo sin documento y sin líneas (400 WarehouseFixed); al cambiarlo se limpian posición y muelle y el almacén
    ///   nuevo debe estar activo (422) y tener zona STAGING si no se indica posición (422 NoStagingBin);
    /// - posición de recepción por defecto (STAGING o CROSSDOCK del almacén) o ClearStagingBin; muelle del almacén o ClearDock;
    /// - transporte y referencia: '' = borrar, máximo 80 (400).
    /// </summary>
    public async Task<ReceiptDetailDto> UpdateHeaderAsync(Guid publicId, ReceiptHeaderUpdateRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var current = await ResolveAsync(publicId, ct);
        Warehouse? requested = req.WarehousePublicId is Guid wpid ? await ReceivingSupport.ResolveWarehouseOrDefaultAsync(db, wpid, ct) : null;

        await db.RunInTransactionAsync(async ct2 =>
        {
            var r = await LockOpenAsync(current.ReceiptHeaderId, ct2);
            PurchasingSupport.EnsureRowVersion(r.RowVersion, req.RowVersion, DbExtensions.ConcurrencyMessage);
            var hasDocument = r.AsnId is not null;
            var errors = new Dictionary<string, string[]>();

            int? newTypeId = null;
            if (!string.IsNullOrWhiteSpace(req.Type))
            {
                var currentType = (await lookups.GetAsync(r.ReceiptTypeLookupId, ct2))?.InternalCode ?? ReceiptTypes.Blind;
                if (hasDocument)
                {
                    if (!string.Equals(req.Type.Trim(), currentType, StringComparison.OrdinalIgnoreCase))
                        errors["type"] = new[] { ReceiptRules.TypeFixedWithDocument };
                }
                else
                {
                    var (t, typeError) = ReceiptRules.ParseManualType(req.Type);
                    if (typeError is not null) errors["type"] = new[] { typeError };
                    else if (t != currentType) newTypeId = await lookups.GetIdAsync(LookupDomains.ReceiptType, t!, ct2);
                }
            }

            var warehouseChanged = false;
            if (requested is not null && requested.WarehouseId != r.WarehouseId)
            {
                var hasLines = await db.Set<ReceiptLine>().AsNoTracking().AnyAsync(l => l.ReceiptHeaderId == r.ReceiptHeaderId, ct2);
                if (hasDocument || hasLines) errors["warehousePublicId"] = new[] { ReceiptRules.WarehouseFixed };
                else warehouseChanged = true;
            }

            var (carrierChanged, carrier, carrierError) = ReceiptRules.PatchText(req.Carrier, ReceiptRules.CarrierTooLong);
            if (carrierError is not null) errors["carrier"] = new[] { carrierError };
            var (referenceChanged, reference, referenceError) = ReceiptRules.PatchText(req.Reference, ReceiptRules.ReferenceTooLong);
            if (referenceError is not null) errors["reference"] = new[] { referenceError };
            // Lote 16 (D1): modo SOLO de este recibo abierto (el del almacén no cambia).
            var (newMode, modeError) = ReceivingModeRules.ParseMode(req.ReceivingMode);
            if (modeError is not null) errors["receivingMode"] = new[] { modeError };
            if (errors.Count > 0) throw new ValidationException(errors);
            var effectiveMode = newMode ?? await ReceiptModeAsync(r, ct2);

            if (warehouseChanged)
            {
                if (!requested!.IsActive) throw new StatusRuleException(ReceiptRules.WarehouseInactive);
                r.WarehouseId = requested.WarehouseId;
                r.DefaultStagingBinId = null;
                r.DockId = null;
                // El almacén nuevo debe poder recibir (misma regla que el alta). Lote 16: en directo no exige STAGING.
                if (req.StagingBinId is null && !ReceivingModeRules.IsDirect(effectiveMode))
                    await ReceivingSupport.DefaultStagingBinAsync(db, r.WarehouseId, ct2);
            }
            if (newTypeId is int typeId) r.ReceiptTypeLookupId = typeId;
            if (req.ClearStagingBin == true) r.DefaultStagingBinId = null;
            if (req.StagingBinId is int sb)
                r.DefaultStagingBinId = (await ReceivingSupport.ResolveStagingBinAsync(db, r.WarehouseId, sb, "stagingBinId", ct2)).BinId;
            if (req.ClearDock == true) r.DockId = null;
            if (req.DockId is int dockId)
            {
                await EnsureDockAsync(r.WarehouseId, dockId, ct2);
                r.DockId = dockId;
            }
            if (carrierChanged) r.Carrier = carrier;
            if (referenceChanged) r.Reference = reference;
            if (newMode is not null)
            {
                // Pasar a "Con acomodo" exige una posición de recepción: la del encabezado o la del almacén (422 NoStagingBin).
                if (!ReceivingModeRules.IsDirect(newMode) && r.DefaultStagingBinId is null)
                    await ReceivingSupport.DefaultStagingBinAsync(db, r.WarehouseId, ct2);
                r.ReceivingModeLookupId = await ModeIdAsync(newMode, ct2);
            }
            await SaveReceiptAsync(ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ================================================================ edición de líneas (solo abiertos: EXPECTED, RECEIVING, DISCREPANCY)

    /// <summary>
    /// Captura de una línea del recibo abierto: cantidad recibida (≥ 0), lote (EnsureLot) o quitarlo, series normalizadas y
    /// posición de recepción. En SERIAL, capturar las series sin cantidad fija la cantidad = número de series. Bajar lo
    /// recibido por debajo de lo asignado a cruce de muelle se permite: el faltante se ve al confirmar (D29).
    /// Lote 13: cambio de producto (solo en líneas sin línea del aviso; mismo dueño que un alta de línea; con cruce de muelle
    /// → 409; lote y series se limpian si no vienen) y esperado (ExpectedQty / ClearExpected) solo en recibos ciegos o de
    /// devolución. Al guardar se sincroniza el estatus abierto (RECEIVING ↔ DISCREPANCY).
    /// </summary>
    public async Task<ReceiptDetailDto> UpdateLineAsync(Guid publicId, int lineId, ReceiptLineUpdateRequest req, CancellationToken ct)
    {
        var current = await ResolveAsync(publicId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var r = await LockOpenAsync(current.ReceiptHeaderId, ct2);
            var line = await db.Set<ReceiptLine>().FirstOrDefaultAsync(l => l.ReceiptLineId == lineId && l.ReceiptHeaderId == r.ReceiptHeaderId, ct2)
                       ?? throw new NotFoundException(ReceiptRules.LineNotFoundWhat, feminine: true);
            var product = await db.Set<Product>().AsNoTracking().FirstAsync(p => p.ProductId == line.ProductId, ct2);
            var hasDocument = r.AsnId is not null;
            var errors = new Dictionary<string, string[]>();

            var productChanged = false;
            if (req.ProductPublicId is Guid newProductPublicId && newProductPublicId != product.PublicId)
            {
                if (line.AsnLineId is not null) throw new ValidationException("productPublicId", ReceiptRules.DocumentLineProductFixed);
                var next = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == newProductPublicId, ct2)
                           ?? throw new NotFoundException("Producto");
                if (!next.IsActive) throw new StatusRuleException(ReceiptRules.ProductInactive(next.Sku));
                var (owner, ownerClientId) = await OwnerRuleOfAsync(r, ct2);
                var ownerError = OwnerError(owner, ownerClientId, next);
                if (ownerError is not null) throw new ValidationException("productPublicId", ownerError);
                if ((await CrossDockByLineAsync(new List<int> { line.ReceiptLineId }, ct2)).Count > 0)
                    throw new ConflictException(ReceiptRules.LineHasCrossDockProductChange);
                product = next;
                productChanged = true;
            }
            var trackingCode = await TrackingOfAsync(product, ct2);

            var expected = line.ExpectedQty;
            if (req.ExpectedQty is not null || req.ClearExpected == true)
            {
                if (hasDocument) errors["expectedQty"] = new[] { ReceiptRules.ExpectedOnlyWithoutDocument };
                else if (req.ExpectedQty is decimal eq)
                {
                    var ee = ReceiptRules.ValidateManualExpectedQty(eq, trackingCode, product.Sku);
                    if (ee is not null) errors["expectedQty"] = new[] { ee };
                    else expected = eq;
                }
                else expected = null;
            }

            var received = line.ReceivedQty;
            if (req.ReceivedQty is not null)
            {
                var e = ReceiptRules.ValidateReceivedQty(req.ReceivedQty);
                if (e is not null) errors["receivedQty"] = new[] { e };
                else received = req.ReceivedQty.Value;
            }

            // Con cambio de producto, el lote y las series del producto anterior no aplican: se limpian si no vienen.
            var serials = productChanged ? Array.Empty<string>() : ParseSerials(line.SerialNumbersJson);
            if (req.SerialNumbers is not null)
            {
                var (norm, se) = ReceiptRules.NormalizeSerials(req.SerialNumbers);
                if (se is not null) errors["serialNumbers"] = new[] { se };
                else
                {
                    serials = norm;
                    if (req.ReceivedQty is null && trackingCode == TrackingTypes.Serial) received = norm.Count;
                }
            }

            var lotId = productChanged ? null : line.LotId;
            if (req.ClearLot == true) lotId = null;
            if (req.Lot is not null)
            {
                if (trackingCode != TrackingTypes.Lot && trackingCode != TrackingTypes.Serial) errors["lot"] = new[] { ReceiptRules.LotNotAllowed(product.Sku) };
                else
                {
                    var (n, le) = ReceiptRules.NormalizeLot(req.Lot.Number, req.Lot.ManufactureDate, req.Lot.ExpiryDate);
                    if (le is not null) errors["lot"] = new[] { le };
                    else lotId = await ReceivingSupport.EnsureLotAsync(db, product.ProductId, n!, req.Lot.ManufactureDate, req.Lot.ExpiryDate, ct2);
                }
            }

            if (errors.Count == 0)
            {
                var te = ReceiptRules.ValidateCapture(trackingCode, product.Sku, received, lotId is not null, serials.Count);
                if (te is not null) errors["line"] = new[] { te };
            }
            if (req.StagingBinId is int sb && errors.Count == 0)
                line.StagingBinId = (await ReceivingSupport.ResolveStagingBinAsync(db, r.WarehouseId, sb, "stagingBinId", ct2)).BinId;
            // Lote 16: posición destino (ClearTargetBin la quita; TargetBinId la fija con las reglas de ResolveTargetBinAsync).
            var targetId = req.ClearTargetBin == true ? null : line.TargetBinId;
            if (req.TargetBinId is int tb && errors.Count == 0)
            {
                var target = await ResolveTargetBinAsync(r.WarehouseId, tb, null, "targetBinId", "targetBinCode", errors, ct2);
                if (target is not null) targetId = target.BinId;
            }
            // 2026-10-08: daño declarado. ClearDamage lo quita; DamagedQty lo reemplaza completo (causa, comentario, posición, desechar);
            // sin ninguno de los dos se conserva, pero no puede quedar mayor que lo recibido.
            var damageChanged = false;
            LineDamage? newDamage = null;
            if (errors.Count == 0)
            {
                if (req.ClearDamage == true) damageChanged = true;
                else if (req.DamagedQty is not null)
                {
                    damageChanged = true;
                    newDamage = await ResolveDamageAsync(r.WarehouseId, req.DamagedQty, received, trackingCode == TrackingTypes.Serial, req.DamageCause, req.DamageNote,
                        req.DamageBinId, null, req.DamageDiscard, "line", errors, ct2);
                }
                else if (line.DamagedQty > received) errors["line.damagedQty"] = new[] { DamageRules.LineDamagedTooMuch };
            }
            if (errors.Count > 0) throw new ValidationException(errors);
            if (damageChanged) ApplyDamage(line, newDamage);

            line.TargetBinId = targetId;
            line.ProductId = product.ProductId;
            line.ExpectedQty = expected;
            line.ReceivedQty = received;
            line.LotId = lotId;
            line.SerialNumbersJson = serials.Count == 0 ? null : JsonSerializer.Serialize(serials, Json);
            await SaveReceiptAsync(ct2);
            await SyncOpenStatusAsync(r, ct2);
            await SaveReceiptAsync(ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>
    /// Línea extra (sin línea del aviso) en un recibo abierto, hasta 200 líneas. En un recibo contra ASN de cliente el
    /// producto debe ser de ese cliente (400 OwnerMismatch); contra PO solo productos propios (400 OwnProductsOnly).
    /// Lote 13: ExpectedQty solo en recibos ciegos o de devolución (400 'line.expectedQty'); posición de recepción: la de la
    /// solicitud, si no la del encabezado, si no la de otra línea o la STAGING por defecto. Sincroniza el estatus abierto.
    /// </summary>
    public async Task<ReceiptDetailDto> AddLineAsync(Guid publicId, ReceiptLineRequest req, CancellationToken ct)
    {
        var current = await ResolveAsync(publicId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var r = await LockOpenAsync(current.ReceiptHeaderId, ct2);
            var count = await db.Set<ReceiptLine>().CountAsync(l => l.ReceiptHeaderId == r.ReceiptHeaderId, ct2);
            if (count >= ReceiptRules.MaxLines) throw new ValidationException("lines", ReceiptRules.TooManyLines);

            var (owner, ownerClientId) = await OwnerRuleOfAsync(r, ct2);
            var errors = new Dictionary<string, string[]>();
            var line = await BuildLineAsync(r.WarehouseId, req, "line", owner, ownerClientId, r.AsnId is null, errors, ct2);
            if (errors.Count > 0 || line is null) throw new ValidationException(errors);
            line.ReceiptHeaderId = r.ReceiptHeaderId;
            // Lote 16: en un recibo directo la posición de recepción no se exige (solo sirve para el cruce de muelle, D11).
            line.StagingBinId ??= r.DefaultStagingBinId ?? (ReceivingModeRules.IsDirect(await ReceiptModeAsync(r, ct2))
                ? await DefaultLineStagingOrNullAsync(r, ct2)
                : await DefaultLineStagingAsync(r, ct2));
            db.Set<ReceiptLine>().Add(line);
            await SaveReceiptAsync(ct2);
            await SyncOpenStatusAsync(r, ct2);
            await SaveReceiptAsync(ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>
    /// Elimina una línea extra de un recibo abierto. Las del aviso no se eliminan (409); con cruce de muelle asignado → 409.
    /// Lote 13: sincroniza el estatus (sin líneas queda en RECEIVING: un recibo no vuelve a EXPECTED).
    /// </summary>
    public async Task<ReceiptDetailDto> RemoveLineAsync(Guid publicId, int lineId, CancellationToken ct)
    {
        var current = await ResolveAsync(publicId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var r = await LockOpenAsync(current.ReceiptHeaderId, ct2);
            var line = await db.Set<ReceiptLine>().FirstOrDefaultAsync(l => l.ReceiptLineId == lineId && l.ReceiptHeaderId == r.ReceiptHeaderId, ct2)
                       ?? throw new NotFoundException(ReceiptRules.LineNotFoundWhat, feminine: true);
            if (line.AsnLineId is not null) throw new ConflictException(ReceiptRules.AsnLineNotRemovable);
            if ((await CrossDockByLineAsync(new List<int> { line.ReceiptLineId }, ct2)).Count > 0)
                throw new ConflictException(ReceiptRules.LineHasCrossDock);
            // Línea de un recibo abierto: aún no hay movimiento que la referencie; se retira del documento.
            db.Set<ReceiptLine>().Remove(line);
            await SaveReceiptAsync(ct2);
            await SyncOpenStatusAsync(r, ct2);
            await SaveReceiptAsync(ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ================================================================ confirmación

    public async Task<ReceiptDetailDto> ConfirmAsync(Guid publicId, ReceiptConfirmRequest? req, CancellationToken ct)
    {
        await ConfirmCoreAsync(publicId, req, ct);
        return await GetAsync(publicId, ct);
    }

    private async Task ConfirmCoreAsync(Guid publicId, ReceiptConfirmRequest? req, CancellationToken ct)
    {
        var current = await ResolveAsync(publicId, ct);
        var comment = string.IsNullOrWhiteSpace(req?.Comment) ? null : req!.Comment!.Trim();
        var receiptEntityTypeId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.Receipt, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            // 1-2. Encabezado bloqueado y re-verificación: la segunda de dos confirmaciones simultáneas ve un estatus
            // confirmado → 422.
            var r = await LockOpenAsync(current.ReceiptHeaderId, ct2);
            db.ApplyRowVersion(r, req?.RowVersion);

            // 3. ASN después del recibo (orden del lote: Receipt < Asn < PO).
            Asn? asn = r.AsnId is int asnId ? await ReceivingSupport.LockAsnAsync(db, asnId, ct2) : null;

            var lines = await db.Set<ReceiptLine>().Where(l => l.ReceiptHeaderId == r.ReceiptHeaderId).OrderBy(l => l.ReceiptLineId).ToListAsync(ct2);
            if (lines.Count == 0) throw new StatusRuleException(ReceiptRules.NoLinesToConfirm);
            var typeCode = (await lookups.GetAsync(r.ReceiptTypeLookupId, ct2))?.InternalCode ?? ReceiptTypes.Blind;
            var expects = ReceiptRules.ExpectsQuantities(typeCode);
            // Lote 13: primero el estatus abierto al día (RECEIVING o DISCREPANCY según las líneas), para que el camino a la
            // confirmación salga de él (DISCREPANCY → RECEIVED_VARIANCE; RECEIVING → RECEIVED).
            var enabled = await EnabledReceiptStatusesAsync(ct2);
            await SyncOpenStatusAsync(r, ct2, enabled);
            var hasVariance = lines.Any(l => ReceiptRules.HasVariance(expects, l.ExpectedQty, l.ReceivedQty));

            var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
            var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct2);
            var tracking = await ReceivingSupport.TrackingCodesAsync(db, products.Values.Select(p => p.TrackingTypeLookupId), ct2);

            // Lote 16: modo del recibo. En directo, las líneas con cruce de muelle asignado entran a la posición de recepción
            // (D11); las demás, a su posición destino, que se revalida aquí (zona de guardado y activa).
            var direct = ReceivingModeRules.IsDirect(await ReceiptModeAsync(r, ct2));
            var plannedCrossDock = direct
                ? await CrossDockByLineAsync(lines.Select(l => l.ReceiptLineId).ToList(), ct2)
                : new Dictionary<int, decimal>();
            var targets = direct
                ? await TargetBinsAsync(r.WarehouseId, lines.Where(l => l.TargetBinId != null).Select(l => l.TargetBinId!.Value), ct2)
                : new Dictionary<int, TargetBinInfo>();
            bool EntersTarget(ReceiptLine l) => direct && !plannedCrossDock.ContainsKey(l.ReceiptLineId);

            // 4. Seguimiento de cada línea (400 con Errors por línea, sin escribir nada). Lote 16: en directo, también la
            // posición destino de cada línea que recibe algo (junto a los errores de seguimiento).
            var errors = new Dictionary<string, string[]>();
            var serialsByLine = new Dictionary<int, IReadOnlyList<string>>();
            for (var i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                var p = products[l.ProductId];
                var serials = ParseSerials(l.SerialNumbersJson);
                serialsByLine[l.ReceiptLineId] = serials;
                var code = tracking.GetValueOrDefault(p.TrackingTypeLookupId, TrackingTypes.None);
                var te = ReceiptRules.ValidateTracking(code, p.Sku, l.ReceivedQty, l.LotId is not null, serials.Count);
                if (te is not null) errors[$"lines[{i}]"] = new[] { te };
                if (l.DamagedQty > l.ReceivedQty) errors[$"lines[{i}].damagedQty"] = new[] { DamageRules.LineDamagedTooMuch };
                if (!EntersTarget(l)) continue;
                if (l.TargetBinId is int tb && targets.TryGetValue(tb, out var target))
                {
                    var ze = ReceivingModeRules.ValidateTargetZone(target.Code, target.ZoneTypeCode);
                    if (ze is not null) errors[$"lines[{i}].targetBinId"] = new[] { ze };
                }
                else if (ReceivingModeRules.NeedsTarget(code, l.ReceivedQty, l.LotId is not null))
                {
                    errors[$"lines[{i}].targetBinId"] = new[] { ReceivingModeRules.TargetRequired(p.Sku) };
                }
            }
            if (errors.Count > 0) throw new ValidationException(errors);
            // Lote 16: posición destino dada de baja (o su zona) después de elegirla → 422 al confirmar.
            foreach (var l in lines.Where(EntersTarget))
            {
                if (l.TargetBinId is not int tb || !targets.TryGetValue(tb, out var target)) continue;
                if (!target.IsActive) throw new StatusRuleException(ReceivingModeRules.TargetBinInactive(target.Code));
                if (!target.ZoneActive) throw new StatusRuleException(ReceivingModeRules.TargetZoneInactive(target.Code));
            }

            // 7 (antes de asentar). PO: lo recibido por línea de PO (las líneas extra no cuentan contra la PO); P8 bloquea la PO
            // y la avanza a PARTIAL/RECEIVED. Va ANTES del ledger para respetar el orden de bloqueo único del lote
            // (encabezados Receipt < Asn < PurchaseOrder y SOLO DESPUÉS saldos): así no se invierte contra quien bloquea la PO
            // y luego asienta (resolución de faltantes). El resultado es el mismo: todo corre en esta transacción.
            if (asn?.PurchaseOrderId is int poId)
            {
                var asnLineIds = lines.Where(l => l.AsnLineId != null).Select(l => l.AsnLineId!.Value).ToList();
                var poLineByAsnLine = await db.Set<AsnLine>().AsNoTracking()
                    .Where(al => al.AsnId == asn.AsnId && asnLineIds.Contains(al.AsnLineId) && al.PurchaseOrderLineId != null)
                    .ToDictionaryAsync(al => al.AsnLineId, al => al.PurchaseOrderLineId!.Value, ct2);
                var quantities = lines
                    .Where(l => l.AsnLineId is int al && poLineByAsnLine.ContainsKey(al) && l.ReceivedQty > 0m)
                    .GroupBy(l => poLineByAsnLine[l.AsnLineId!.Value])
                    .Select(g => new PurchaseOrderReceiptQty(g.Key, g.Sum(l => l.ReceivedQty)))
                    .ToList();
                if (quantities.Count > 0) await purchaseOrders.ApplyReceiptAsync(poId, quantities, ct2);
            }

            // 5. Asientos en MAGNITUD (el ledger pone el signo), To/From la posición de recepción, Ref RECEIPT + id.
            // Lote 16: en directo, To/From la posición destino (RECEIPT y RECEIPT_VARIANCE ahí mismo, sin tareas); una línea
            // con cruce de muelle asignado entra a la de recepción como siempre (D11).
            int? defaultStaging = null;
            int? optionalStaging = null;
            var optionalStagingResolved = false;
            var postings = new List<InventoryPosting>();
            var owners = new List<(ReceiptLine Line, PlannedReceiptPosting Planned)>();
            var landingByLine = new Dictionary<int, int?>();   // posición donde aterriza cada línea (para sacar de ahí lo dañado)
            foreach (var l in lines)
            {
                var p = products[l.ProductId];
                var code = tracking.GetValueOrDefault(p.TrackingTypeLookupId, TrackingTypes.None);
                // LOT recibido en 0 sin lote: no mueve inventario (la varianza queda visible en la línea).
                if (code == TrackingTypes.Lot && l.LotId is null) continue;
                int? bin;
                if (EntersTarget(l))
                {
                    bin = ReceivingModeRules.PostingBin(ReceivingModes.Direct, false, l.TargetBinId, l.StagingBinId);
                    if (bin is null)
                    {
                        // Sin destino solo llega una línea que no recibió nada: el par RECEIPT/ajuste de su faltante (neto 0) se
                        // asienta en la posición de recepción si el almacén tiene una; si no, se omite (la diferencia sigue
                        // visible en la línea y en la orden de compra).
                        if (!optionalStagingResolved)
                        {
                            optionalStaging = await ReceivingSupport.TryDefaultStagingBinAsync(db, r.WarehouseId, ct2);
                            optionalStagingResolved = true;
                        }
                        bin = l.StagingBinId ?? optionalStaging;
                        if (bin is null) continue;
                    }
                }
                else
                {
                    if (l.StagingBinId is null)
                    {
                        defaultStaging ??= await ReceivingSupport.DefaultStagingBinAsync(db, r.WarehouseId, ct2);
                        l.StagingBinId = defaultStaging;
                    }
                    bin = l.StagingBinId;
                }
                landingByLine[l.ReceiptLineId] = bin;
                foreach (var planned in ReceiptPostingRules.Plan(expects, l.ExpectedQty, l.ReceivedQty, code, serialsByLine[l.ReceiptLineId]))
                {
                    postings.Add(new InventoryPosting(planned.TxnType, l.ProductId, planned.Quantity,
                        LotId: l.LotId, SerialNumber: planned.SerialNumber,
                        FromWarehouseId: planned.Inbound ? null : r.WarehouseId, FromBinId: planned.Inbound ? null : bin,
                        ToWarehouseId: planned.Inbound ? r.WarehouseId : null, ToBinId: planned.Inbound ? bin : null,
                        RefEntityType: EntityTypes.Receipt, RefId: r.ReceiptHeaderId, ReasonCode: planned.ReasonCode));
                    owners.Add((l, planned));
                }
            }
            IReadOnlyList<long> txnIds = postings.Count == 0 ? Array.Empty<long>() : await ledger.PostAsync(postings, ct2);

            // 6. AdjustmentTxnId = primer ajuste de varianza de la línea.
            for (var i = 0; i < owners.Count; i++)
                if (owners[i].Planned.TxnType == InventoryTxnTypes.Adjustment && owners[i].Line.AdjustmentTxnId is null)
                    owners[i].Line.AdjustmentTxnId = txnIds[i];

            // 8. ASN → RECEIVED.
            if (asn is not null)
            {
                var asnTo = await statuses.TransitionAsync(StatusDomains.AsnStatus, EntityTypes.Asn, asn.AsnId, asn.StatusCodeId, AsnStatuses.Received, null, ct2);
                asn.StatusCodeId = asnTo.StatusCodeId;
            }

            // 9. Recibo abierto → RECEIVED (o RECEIVED_VARIANCE si hay diferencia, Lote 13) con fecha de confirmación (insumo
            // de la Contabilización de compras, Lote 10). El comentario va en el último paso.
            var currentCode = await StatusCodeOfAsync(r.StatusCodeId, ct2);
            var path = ReceiptStatusRules.Path(currentCode, ReceiptStatusRules.ConfirmedTarget(hasVariance, enabled), enabled);
            for (var i = 0; i < path.Count; i++)
            {
                var step = await statuses.TransitionAsync(StatusDomains.ReceiptStatus, EntityTypes.Receipt, r.ReceiptHeaderId, r.StatusCodeId,
                    path[i], i == path.Count - 1 ? comment : null, ct2);
                r.StatusCodeId = step.StatusCodeId;
            }
            r.ReceivedAtUtc = DateTime.UtcNow;
            r.ReceivedBy = tenant.UserId;
            await SaveReceiptAsync(ct2);

            // 10. Cruce de muelle: lo cubierto por asignaciones se queda en staging (reservado) y no va a putaway.
            var crossDock = new Dictionary<int, decimal>();
            foreach (var participant in participants)
            {
                var taken = await participant.OnReceiptConfirmedAsync(r, lines, ct2);
                foreach (var (lineId, qty) in taken) crossDock[lineId] = crossDock.GetValueOrDefault(lineId) + qty;
            }

            // 11. PUTAWAY por el remanente hacia la posición destino de la línea (Lote 16) o, sin ella, la sugerida
            // (preferida, consolidar, rotación, reserva). En directo solo las líneas con cruce de muelle (D11): las demás ya
            // quedaron en su posición destino.
            var created = 0;
            foreach (var l in lines)
            {
                if (EntersTarget(l)) continue;
                // lo dañado no se acomoda: se queda (o se mueve) según su reporte
                var remaining = l.ReceivedQty - l.DamagedQty - crossDock.GetValueOrDefault(l.ReceiptLineId);
                if (remaining <= 0m) continue;
                var p = products[l.ProductId];
                if (tracking.GetValueOrDefault(p.TrackingTypeLookupId, TrackingTypes.None) == TrackingTypes.Lot && l.LotId is null) continue;
                var toBin = l.TargetBinId ?? await SuggestAsync(r.WarehouseId, l.ProductId, l.LotId, remaining, l.StagingBinId, ct2);
                await CreatePutawayAsync(r, l, remaining, toBin, ct2);
                created++;
            }

            // 12. Sin ninguna PUTAWAY (todo a cruce de muelle, recibido 0 o, Lote 16, recibo directo) → PUTAWAY en la misma
            // transacción (desde RECEIVED es la siguiente etapa; desde RECEIVED_VARIANCE, la entrada lateral sembrada; D7:
            // historial con los dos pasos).
            if (created == 0)
            {
                var done = await statuses.TransitionAsync(StatusDomains.ReceiptStatus, EntityTypes.Receipt, r.ReceiptHeaderId, r.StatusCodeId, ReceiptStatuses.Putaway, null, ct2);
                r.StatusCodeId = done.StatusCodeId;
            }
            await SaveReceiptAsync(ct2);

            // 13. Daño declarado en las líneas: un reporte DAN-##### por línea; las unidades salen de donde aterrizaron (desecho o cuarentena). Va al
            // final porque el ledger puede limpiar el seguimiento del contexto: lo que se necesita se copia antes y la línea se vuelve a leer.
            var damaged = lines.Where(l => l.DamagedQty > 0m && l.DamageReportId is null && l.DamageCauseLookupId != null)
                .Select(l => (l.ReceiptLineId, l.ProductId, l.LotId, Qty: l.DamagedQty, Cause: l.DamageCauseLookupId!.Value, l.DamageNote, l.DamageBinId, l.DamageDiscard,
                    Landing: landingByLine.GetValueOrDefault(l.ReceiptLineId))).ToList();
            var receiptId = r.ReceiptHeaderId;
            var warehouseId = r.WarehouseId;
            foreach (var x in damaged)
            {
                var reportId = await damages.ReportReceiptLineAsync(warehouseId, receiptId, x.ProductId, x.LotId, x.Qty, x.Cause, x.DamageNote, x.Landing,
                    x.DamageBinId, x.DamageDiscard, ct2);
                var fresh = await db.Set<ReceiptLine>().FirstAsync(l => l.ReceiptLineId == x.ReceiptLineId, ct2);
                fresh.DamageReportId = reportId;
                await SaveReceiptAsync(ct2);
            }
        }, ct);
    }

    // ================================================================ Lote 16: posición destino sugerida

    /// <summary>
    /// Posiciones destino sugeridas para una línea (take 1..10; por defecto 5): el acomodo dirigido (preferida, mismo lote,
    /// mismo producto, picking vacía si rota mucho, reserva vacía, reserva con espacio; en devoluciones la cuarentena primero,
    /// D6) con la cantidad de la línea (lo recibido; si es 0, lo esperado; si no, 1) y el cupo descontando lo que otras líneas
    /// del recibo abierto ya destinan a cada posición. Las que caben primero; las que exceden el cupo al final con Fits =
    /// false (D4: se puede exceder con aviso). Línea de otro recibo → 404.
    /// </summary>
    public async Task<IReadOnlyList<ReceiptTargetSuggestionDto>> SuggestTargetsAsync(Guid publicId, int lineId, int? take, CancellationToken ct)
    {
        var r = await ResolveAsync(publicId, ct);
        var line = await db.Set<ReceiptLine>().AsNoTracking().FirstOrDefaultAsync(l => l.ReceiptLineId == lineId && l.ReceiptHeaderId == r.ReceiptHeaderId, ct)
                   ?? throw new NotFoundException(ReceiptRules.LineNotFoundWhat, feminine: true);
        var n = take is int t && t > 0 ? Math.Min(t, ReceivingModeRules.MaxSuggestions) : ReceivingModeRules.DefaultSuggestions;
        var open = ReceiptStatusRules.IsOpen(await StatusCodeOfAsync(r.StatusCodeId, ct));
        var claimed = open
            ? await ClaimedByBinAsync(r.ReceiptHeaderId, lineId, null, ct)
            : new Dictionary<int, decimal>();
        var claimedSame = open
            ? await ClaimedByBinAsync(r.ReceiptHeaderId, lineId, line.ProductId, ct)
            : new Dictionary<int, decimal>();
        var qty = line.ReceivedQty > 0m ? line.ReceivedQty : line.ExpectedQty is decimal e && e > 0m ? e : 1m;
        var isReturn = (await lookups.GetAsync(r.ReceiptTypeLookupId, ct))?.InternalCode == ReceiptTypes.Return;
        var suggestions = await suggester.SuggestAsync(new PutawaySuggestionOptions(r.WarehouseId, line.ProductId, line.LotId, qty, null, n,
            claimed, PreferQuarantine: isReturn, IncludeOverCapacity: true, ClaimedSameProductByBin: claimedSame), ct);
        return suggestions.Select(s =>
        {
            var c = claimed.GetValueOrDefault(s.BinId);
            return new ReceiptTargetSuggestionDto(s.BinId, s.BinCode, s.ZoneCode, s.ZoneTypeCode, s.ReasonCode, s.Reason, s.MaxCapacityQty,
                s.BinQty, c, ReceivingModeRules.FreeQty(s.MaxCapacityQty, s.BinQty, c), s.Fits);
        }).ToList();
    }

    /// <summary>
    /// "Usar posiciones sugeridas" (D3: nada se llena solo, solo al pedirlo): con el recibo abierto bloqueado (y rowVersion,
    /// 409), a cada línea sin destino que lo necesita (recibe algo), en orden de id, le asigna la primera sugerida DONDE CABE,
    /// acumulando lo asignado para las siguientes. Devuelve el recibo, las asignadas y las que quedaron sin sugerencia.
    /// </summary>
    public async Task<ReceiptApplySuggestionsResultDto> ApplySuggestedTargetsAsync(Guid publicId, ReceiptApplySuggestionsRequest? req, CancellationToken ct)
    {
        var current = await ResolveAsync(publicId, ct);
        var (assigned, without) = await db.RunInTransactionAsync(async ct2 =>
        {
            var r = await LockOpenAsync(current.ReceiptHeaderId, ct2);
            PurchasingSupport.EnsureRowVersion(r.RowVersion, req?.RowVersion, DbExtensions.ConcurrencyMessage);
            var isReturn = (await lookups.GetAsync(r.ReceiptTypeLookupId, ct2))?.InternalCode == ReceiptTypes.Return;
            var lines = await db.Set<ReceiptLine>().Where(l => l.ReceiptHeaderId == r.ReceiptHeaderId).OrderBy(l => l.ReceiptLineId).ToListAsync(ct2);
            var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
            var trackingByProduct = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId, p => p.TrackingTypeLookupId, ct2);
            var tracking = await ReceivingSupport.TrackingCodesAsync(db, trackingByProduct.Values, ct2);
            var claimed = lines.Where(l => l.TargetBinId != null).GroupBy(l => l.TargetBinId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.ReceivedQty));
            var claimedByProduct = lines.Where(l => l.TargetBinId != null).GroupBy(l => (l.ProductId, Bin: l.TargetBinId!.Value))
                .ToDictionary(g => g.Key, g => g.Sum(l => l.ReceivedQty));
            int a = 0, w = 0;
            foreach (var l in lines)
            {
                if (l.TargetBinId is not null) continue;
                var code = tracking.GetValueOrDefault(trackingByProduct.GetValueOrDefault(l.ProductId), TrackingTypes.None);
                if (!ReceivingModeRules.NeedsTarget(code, l.ReceivedQty, l.LotId is not null)) continue;
                var sameProduct = claimedByProduct.Where(x => x.Key.ProductId == l.ProductId).ToDictionary(x => x.Key.Bin, x => x.Value);
                var s = await suggester.SuggestAsync(new PutawaySuggestionOptions(r.WarehouseId, l.ProductId, l.LotId, l.ReceivedQty, null, 1,
                    claimed, PreferQuarantine: isReturn, ClaimedSameProductByBin: sameProduct), ct2);
                if (s.Count == 0) { w++; continue; }
                l.TargetBinId = s[0].BinId;
                claimed[s[0].BinId] = claimed.GetValueOrDefault(s[0].BinId) + l.ReceivedQty;
                claimedByProduct[(l.ProductId, s[0].BinId)] = claimedByProduct.GetValueOrDefault((l.ProductId, s[0].BinId)) + l.ReceivedQty;
                a++;
            }
            await SaveReceiptAsync(ct2);
            return (a, w);
        }, ct);
        return new ReceiptApplySuggestionsResultDto(await GetAsync(publicId, ct), assigned, without);
    }

    /// <summary>
    /// Lo recibido en las OTRAS líneas del recibo por posición destino (cuenta como ocupado al sugerir); con productId, solo
    /// las de ese producto (cuentan para consolidar con el mismo producto).
    /// </summary>
    private async Task<Dictionary<int, decimal>> ClaimedByBinAsync(int receiptHeaderId, int exceptLineId, int? productId, CancellationToken ct)
        => (await db.Set<ReceiptLine>().AsNoTracking()
                .Where(l => l.ReceiptHeaderId == receiptHeaderId && l.ReceiptLineId != exceptLineId && l.TargetBinId != null
                            && (productId == null || l.ProductId == productId))
                .Select(l => new { Bin = l.TargetBinId!.Value, l.ReceivedQty }).ToListAsync(ct))
            .GroupBy(x => x.Bin).ToDictionary(g => g.Key, g => g.Sum(x => x.ReceivedQty));

    // ================================================================ baja

    /// <summary>
    /// Elimina (IsActive = 0) un recibo abierto (EXPECTED, RECEIVING o DISCREPANCY) sin asignaciones de cruce de muelle. Si su ASN nació de una PO, el ASN se
    /// CANCELA (la PO puede recibirse de nuevo por lo pendiente); un ASN de cliente queda EXPECTED para otro recibo.
    /// </summary>
    public async Task DeleteAsync(Guid publicId, CancellationToken ct)
    {
        var current = await ResolveAsync(publicId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var r = await LockOpenAsync(current.ReceiptHeaderId, ct2);
            var lineIds = await db.Set<ReceiptLine>().AsNoTracking().Where(l => l.ReceiptHeaderId == r.ReceiptHeaderId)
                .Select(l => l.ReceiptLineId).ToListAsync(ct2);
            if ((await CrossDockByLineAsync(lineIds, ct2)).Count > 0) throw new ConflictException(ReceiptRules.ReceiptHasCrossDock);

            r.IsActive = false;
            if (r.AsnId is int asnId)
            {
                var asn = await ReceivingSupport.LockAsnAsync(db, asnId, ct2);
                if (asn.PurchaseOrderId is not null)
                {
                    var to = await statuses.TransitionAsync(StatusDomains.AsnStatus, EntityTypes.Asn, asn.AsnId, asn.StatusCodeId, AsnStatuses.Cancelled,
                        $"Recibo {r.Number} eliminado.", ct2);
                    asn.StatusCodeId = to.StatusCodeId;
                }
            }
            await SaveReceiptAsync(ct2);
        }, ct);
    }

    // ================================================================ helpers

    private enum OwnerRule { Any, Client, OwnOnly }

    private async Task<ReceiptHeader> ResolveAsync(Guid publicId, CancellationToken ct)
        => await db.Set<ReceiptHeader>().AsNoTracking().FirstOrDefaultAsync(r => r.PublicId == publicId && r.IsActive, ct)
           ?? throw new NotFoundException("Recibo");

    /// <summary>Encabezado bloqueado; activo (404 al segundo de dos borrados) y abierto: EXPECTED, RECEIVING o DISCREPANCY (422 ReceiptNotOpen).</summary>
    private async Task<ReceiptHeader> LockOpenAsync(int receiptHeaderId, CancellationToken ct)
    {
        var r = await ReceivingSupport.LockReceiptAsync(db, receiptHeaderId, ct);
        if (!r.IsActive) throw new NotFoundException("Recibo");
        var openIds = await db.StatusIdsAsync(StatusDomains.ReceiptStatus, ReceiptStatuses.OpenCodes, ct);
        if (!openIds.Contains(r.StatusCodeId)) throw new StatusRuleException(ReceiptRules.ReceiptNotOpen(r.Number));
        return r;
    }

    // ---------------------------------------------------------------- Lote 13: estatus abiertos sincronizados

    /// <summary>Códigos de estatus del recibo habilitados para la compañía (los apagados se saltan).</summary>
    private async Task<HashSet<string>> EnabledReceiptStatusesAsync(CancellationToken ct)
        => (await statuses.GetPipelineAsync(StatusDomains.ReceiptStatus, false, ct))
            .Select(s => s.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private async Task<string> StatusCodeOfAsync(int statusCodeId, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct)
           ?? string.Empty;

    /// <summary>
    /// Lleva un recibo abierto al estatus que le corresponde por sus líneas GUARDADAS (ReceiptStatusRules.OpenTarget:
    /// EXPECTED sin líneas si ya estaba, RECEIVING, o DISCREPANCY con diferencia), paso a paso con StatusService (historial
    /// por paso). No guarda: el llamador persiste (los cambios de líneas deben estar guardados antes de llamarla).
    /// </summary>
    private async Task SyncOpenStatusAsync(ReceiptHeader r, CancellationToken ct, IReadOnlyCollection<string>? enabled = null)
    {
        var currentCode = await StatusCodeOfAsync(r.StatusCodeId, ct);
        if (!ReceiptStatusRules.IsOpen(currentCode)) return;
        var typeCode = (await lookups.GetAsync(r.ReceiptTypeLookupId, ct))?.InternalCode ?? ReceiptTypes.Blind;
        var expects = ReceiptRules.ExpectsQuantities(typeCode);
        var lines = await db.Set<ReceiptLine>().AsNoTracking().Where(l => l.ReceiptHeaderId == r.ReceiptHeaderId)
            .Select(l => new { l.ExpectedQty, l.ReceivedQty }).ToListAsync(ct);
        var hasVariance = lines.Any(l => ReceiptRules.HasVariance(expects, l.ExpectedQty, l.ReceivedQty));
        enabled ??= await EnabledReceiptStatusesAsync(ct);
        var target = ReceiptStatusRules.OpenTarget(currentCode, lines.Count, hasVariance, enabled);
        foreach (var step in ReceiptStatusRules.Path(currentCode, target, enabled))
        {
            var to = await statuses.TransitionAsync(StatusDomains.ReceiptStatus, EntityTypes.Receipt, r.ReceiptHeaderId, r.StatusCodeId, step, null, ct);
            r.StatusCodeId = to.StatusCodeId;
        }
    }

    /// <summary>Muelle del almacén del recibo (otro almacén u otro tenant → 404 'Muelle').</summary>
    private async Task EnsureDockAsync(int warehouseId, int dockId, CancellationToken ct)
    {
        var dockOk = await (from d in db.Set<WarehouseDock>().AsNoTracking()
                            join w in db.Set<Warehouse>().AsNoTracking() on d.WarehouseId equals w.WarehouseId
                            where d.WarehouseDockId == dockId && d.WarehouseId == warehouseId
                            select d.WarehouseDockId).AnyAsync(ct);
        if (!dockOk) throw new NotFoundException("Muelle");
    }

    /// <summary>Regla de dueño de las líneas extra: aviso de cliente → productos de ese cliente; orden de compra → propios; sin documento → cualquiera.</summary>
    private async Task<(OwnerRule Owner, int? OwnerClientId)> OwnerRuleOfAsync(ReceiptHeader r, CancellationToken ct)
    {
        if (r.AsnId is not int asnId) return (OwnerRule.Any, null);
        var asn = await db.Set<Asn>().AsNoTracking().FirstAsync(a => a.AsnId == asnId, ct);
        if (asn.PurchaseOrderId is not null) return (OwnerRule.OwnOnly, null);
        return asn.ClientId is int cid ? (OwnerRule.Client, cid) : (OwnerRule.Any, null);
    }

    private static string? OwnerError(OwnerRule owner, int? ownerClientId, Product product)
    {
        if (owner == OwnerRule.Client && product.ClientId != ownerClientId) return ReceiptRules.OwnerMismatch(product.Sku);
        if (owner == OwnerRule.OwnOnly && product.ClientId is not null) return ReceiptRules.OwnProductsOnly(product.Sku);
        return null;
    }

    private async Task<string> TrackingOfAsync(Product product, CancellationToken ct)
        => (await lookups.GetAsync(product.TrackingTypeLookupId, ct))?.InternalCode ?? TrackingTypes.None;

    /// <summary>Una línea por línea del aviso: esperado del aviso, recibido = esperado (R8) y lote del aviso si existe.</summary>
    private async Task<List<ReceiptLine>> LinesFromAsnAsync(Asn asn, CancellationToken ct)
    {
        var asnLines = await db.Set<AsnLine>().AsNoTracking().Where(l => l.AsnId == asn.AsnId).OrderBy(l => l.AsnLineId).ToListAsync(ct);
        if (asnLines.Count == 0) throw new StatusRuleException(ReceiptRules.NoLinesToConfirm);
        if (asnLines.Count > ReceiptRules.MaxLines) throw new ValidationException("lines", ReceiptRules.TooManyLines);
        var productIds = asnLines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        var tracking = await ReceivingSupport.TrackingCodesAsync(db, products.Values.Select(p => p.TrackingTypeLookupId), ct);

        var result = new List<ReceiptLine>(asnLines.Count);
        foreach (var al in asnLines)
        {
            var p = products[al.ProductId];
            if (asn.PurchaseOrderId is not null && p.ClientId is not null)
                throw new ValidationException("purchaseOrderPublicId", ReceiptRules.OwnProductsOnly(p.Sku));
            int? lotId = null;
            if (!string.IsNullOrWhiteSpace(al.LotNumber) && tracking.GetValueOrDefault(p.TrackingTypeLookupId) == TrackingTypes.Lot)
                lotId = await ReceivingSupport.EnsureLotAsync(db, p.ProductId, al.LotNumber.Trim(), null, null, ct);
            result.Add(new ReceiptLine
            {
                AsnLineId = al.AsnLineId, ProductId = al.ProductId, LotId = lotId,
                ExpectedQty = al.ExpectedQty, ReceivedQty = ReceiptRules.InitialReceived(al.ExpectedQty),
            });
        }
        return result;
    }

    /// <summary>
    /// Lote 8A — recibo contra aviso u orden de compra que trae Lines (cola del aparato): lo escaneado manda sobre lo esperado,
    /// como en la captura por pasos. Cada línea de la solicitud se valida igual que una línea extra (producto activo, dueño
    /// según el aviso, cantidad, lote y series; mismos mensajes con la clave lines[i]) y se aplica sobre la línea del aviso
    /// del mismo producto: primero la del mismo lote, si no la primera libre sin lote (o cualquiera libre del producto si la
    /// solicitud no trae lote). Si ya no queda línea libre del producto, se suma a la ya aplicada del mismo producto (y lote);
    /// si el producto no está en el aviso, entra como línea extra (no cuenta contra la PO). Las líneas del aviso que la
    /// solicitud no menciona quedan recibidas en 0 (faltante visible al confirmar). Máximo 200 líneas en total.
    /// </summary>
    private async Task ApplyRequestLinesToAsnAsync(int warehouseId, Asn asn, List<ReceiptLine> lines, IReadOnlyList<ReceiptLineRequest> requestLines,
        CancellationToken ct)
    {
        if (requestLines.Count > ReceiptRules.MaxLines) throw new ValidationException("lines", ReceiptRules.TooManyLines);
        var owner = asn.PurchaseOrderId is not null ? OwnerRule.OwnOnly : asn.ClientId is not null ? OwnerRule.Client : OwnerRule.Any;
        var fromAsn = lines.ToList();
        foreach (var l in fromAsn) l.ReceivedQty = 0m;
        var applied = new HashSet<ReceiptLine>();
        var extras = new List<ReceiptLine>();
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < requestLines.Count; i++)
        {
            var key = $"lines[{i}]";
            var built = await BuildLineAsync(warehouseId, requestLines[i], key, owner, asn.ClientId, false, errors, ct);
            if (built is null) continue;

            bool Free(ReceiptLine c) => !applied.Contains(c) && c.ProductId == built.ProductId;
            var match = built.LotId is int lot
                ? fromAsn.FirstOrDefault(c => Free(c) && c.LotId == lot) ?? fromAsn.FirstOrDefault(c => Free(c) && c.LotId == null)
                : fromAsn.FirstOrDefault(Free);
            if (match is not null)
            {
                match.ReceivedQty = built.ReceivedQty;
                match.LotId = built.LotId ?? match.LotId;
                match.SerialNumbersJson = built.SerialNumbersJson;
                match.StagingBinId = built.StagingBinId ?? match.StagingBinId;
                match.TargetBinId = built.TargetBinId ?? match.TargetBinId;   // Lote 16: la posición destino escaneada
                CopyDamage(built, match);
                applied.Add(match);
                continue;
            }

            var same = fromAsn.FirstOrDefault(c => applied.Contains(c) && c.ProductId == built.ProductId && (built.LotId is null || c.LotId == built.LotId));
            if (same is null) { extras.Add(built); continue; }
            // Lote 16 (H11): la línea del documento entra a UNA sola posición; el mismo producto con otro destino → 400.
            if (built.TargetBinId is int bt && same.TargetBinId is int st && bt != st)
            {
                var sku = await db.Set<Product>().AsNoTracking().Where(p => p.ProductId == built.ProductId).Select(p => p.Sku).FirstAsync(ct);
                var binCode = await db.Set<WarehouseBin>().AsNoTracking().Where(b => b.WarehouseBinId == st && b.WarehouseId == warehouseId)
                    .Select(b => b.Code).FirstOrDefaultAsync(ct) ?? st.ToString(System.Globalization.CultureInfo.InvariantCulture);
                errors[key + ".targetBinCode"] = new[] { ReceivingModeRules.SameProductOtherTarget(sku, binCode) };
                continue;
            }
            same.TargetBinId ??= built.TargetBinId;
            var (serials, se) = ReceiptRules.NormalizeSerials(ParseSerials(same.SerialNumbersJson).Concat(ParseSerials(built.SerialNumbersJson)));
            if (se is not null) { errors[key + ".serialNumbers"] = new[] { se }; continue; }
            same.ReceivedQty += built.ReceivedQty;
            same.SerialNumbersJson = serials.Count == 0 ? null : JsonSerializer.Serialize(serials, Json);
            // lo dañado de la nueva captura se suma; la causa, el comentario y la posición son los de la primera que los trajo
            if (built.DamagedQty > 0m)
            {
                var hadDamage = same.DamagedQty > 0m;
                same.DamagedQty += built.DamagedQty;
                if (!hadDamage) CopyDamage(built, same, qtyToo: false);
                else same.DamageNote = same.DamageNote is null ? built.DamageNote : built.DamageNote is null ? same.DamageNote : $"{same.DamageNote} · {built.DamageNote}";
                if (same.DamageNote is { Length: > DamageRules.NotesMax }) same.DamageNote = same.DamageNote[..DamageRules.NotesMax];
            }
        }
        if (errors.Count > 0) throw new ValidationException(errors);
        if (lines.Count + extras.Count > ReceiptRules.MaxLines) throw new ValidationException("lines", ReceiptRules.TooManyLines);
        lines.AddRange(extras);
    }

    /// <summary>
    /// Línea de la solicitud (ciega, devolución o extra): producto activo, dueño según el origen, cantidad, lote y series.
    /// Lote 13: allowExpected = recibo sin documento (ciego o devolución): admite ExpectedQty (≥ 0, 3 decimales, entera en
    /// SERIAL); con documento, traerla → 400 '{key}.expectedQty' (lo esperado viene del aviso o de la orden de compra).
    /// </summary>
    private async Task<ReceiptLine?> BuildLineAsync(int warehouseId, ReceiptLineRequest l, string key, OwnerRule owner, int? ownerClientId,
        bool allowExpected, Dictionary<string, string[]> errors, CancellationToken ct)
    {
        if (l.ExpectedQty is not null && !allowExpected) { errors[key + ".expectedQty"] = new[] { ReceiptRules.ExpectedOnlyWithoutDocument }; return null; }
        if (l.ProductPublicId is not Guid pid) { errors[key + ".productPublicId"] = new[] { "Indique el producto." }; return null; }
        var product = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == pid, ct)
                      ?? throw new NotFoundException("Producto");
        // Producto dado de baja: 422 (el estatus del producto no admite recibir), igual que el ledger al asentar.
        if (!product.IsActive) throw new StatusRuleException(ReceiptRules.ProductInactive(product.Sku));
        var ownerError = OwnerError(owner, ownerClientId, product);
        if (ownerError is not null) { errors[key + ".productPublicId"] = new[] { ownerError }; return null; }
        var trackingCode = await TrackingOfAsync(product, ct);
        if (l.ExpectedQty is decimal expectedQty)
        {
            var ee = ReceiptRules.ValidateManualExpectedQty(expectedQty, trackingCode, product.Sku);
            if (ee is not null) { errors[key + ".expectedQty"] = new[] { ee }; return null; }
        }

        var (serials, se) = ReceiptRules.NormalizeSerials(l.SerialNumbers);
        if (se is not null) { errors[key + ".serialNumbers"] = new[] { se }; return null; }
        var qty = l.ReceivedQty ?? (trackingCode == TrackingTypes.Serial && serials.Count > 0 ? serials.Count : null);
        var qe = ReceiptRules.ValidateReceivedQty(qty);
        if (qe is not null) { errors[key + ".receivedQty"] = new[] { qe }; return null; }

        int? lotId = null;
        if (l.Lot is not null)
        {
            if (trackingCode != TrackingTypes.Lot && trackingCode != TrackingTypes.Serial) { errors[key + ".lot"] = new[] { ReceiptRules.LotNotAllowed(product.Sku) }; return null; }
            var (n, le) = ReceiptRules.NormalizeLot(l.Lot.Number, l.Lot.ManufactureDate, l.Lot.ExpiryDate);
            if (le is not null) { errors[key + ".lot"] = new[] { le }; return null; }
            lotId = await ReceivingSupport.EnsureLotAsync(db, product.ProductId, n!, l.Lot.ManufactureDate, l.Lot.ExpiryDate, ct);
        }
        var te = ReceiptRules.ValidateCapture(trackingCode, product.Sku, qty!.Value, lotId is not null, serials.Count);
        if (te is not null) { errors[key] = new[] { te }; return null; }

        int? stagingId = null;
        if (l.StagingBinId is int sb) stagingId = (await ReceivingSupport.ResolveStagingBinAsync(db, warehouseId, sb, key + ".stagingBinId", ct)).BinId;

        // Lote 16: posición destino por id o por código escaneado.
        var target = await ResolveTargetBinAsync(warehouseId, l.TargetBinId, l.TargetBinCode, key + ".targetBinId", key + ".targetBinCode", errors, ct);
        if (target is null && (l.TargetBinId is not null || !string.IsNullOrWhiteSpace(l.TargetBinCode))) return null;

        var damage = await ResolveDamageAsync(warehouseId, l.DamagedQty, qty.Value, trackingCode == TrackingTypes.Serial, l.DamageCause, l.DamageNote,
            l.DamageBinId, l.DamageBinCode, l.DamageDiscard, key, errors, ct);
        if (errors.Count > 0 && (l.DamagedQty ?? 0m) > 0m && damage is null) return null;

        var line = new ReceiptLine
        {
            ProductId = product.ProductId, LotId = lotId, ReceivedQty = qty.Value, ExpectedQty = l.ExpectedQty, StagingBinId = stagingId,
            SerialNumbersJson = serials.Count == 0 ? null : JsonSerializer.Serialize(serials, Json), TargetBinId = target?.BinId,
        };
        ApplyDamage(line, damage);
        return line;
    }

    // ---------------------------------------------------------------- daño declarado en la línea (2026-10-08)

    /// <summary>Daño ya validado de una línea: cantidad, causa del catálogo, comentario y posición donde se deja (null = la que elija el servidor).</summary>
    private sealed record LineDamage(decimal Qty, int CauseLookupId, string? Note, int? BinId, bool Discard);

    private static void CopyDamage(ReceiptLine from, ReceiptLine to, bool qtyToo = true)
    {
        if (qtyToo) to.DamagedQty = from.DamagedQty;
        to.DamageCauseLookupId = from.DamageCauseLookupId;
        to.DamageNote = from.DamageNote;
        to.DamageBinId = from.DamageBinId;
        to.DamageDiscard = from.DamageDiscard;
    }

    private static void ApplyDamage(ReceiptLine line, LineDamage? damage)
    {
        line.DamagedQty = damage?.Qty ?? 0m;
        line.DamageCauseLookupId = damage?.CauseLookupId;
        line.DamageNote = damage?.Note;
        line.DamageBinId = damage is { Discard: false } ? damage.BinId : null;
        line.DamageDiscard = damage?.Discard ?? false;
    }

    /// <summary>
    /// Valida el daño declarado (reglas puras de DamageRules.ValidateLineDamage) y resuelve la causa y la posición donde se deja: cualquier
    /// posición activa del almacén del recibo (cuarentena, recepción, guardado…), por id o por código. Los errores van a errors con la clave de la
    /// línea; sin cantidad dañada devuelve null sin error.
    /// </summary>
    private async Task<LineDamage?> ResolveDamageAsync(int warehouseId, decimal? damaged, decimal received, bool serialProduct, string? cause, string? note,
        int? binId, string? binCode, bool? discard, string key, Dictionary<string, string[]> errors, CancellationToken ct)
    {
        if (damaged is null or 0m) return null;
        var problems = DamageRules.ValidateLineDamage(damaged, received, serialProduct, cause, note);
        if (problems.Count > 0)
        {
            foreach (var (field, message) in problems) errors[$"{key}.{field}"] = new[] { message };
            return null;
        }
        var (causeCode, _) = DamageRules.ParseCause(cause);
        var (notes, _) = DamageRules.NormalizeNotes(note);
        var causeId = await lookups.GetIdAsync(LookupDomains.DamageCause, causeCode!, ct);
        int? resolvedBin = null;
        if (discard != true && (binId is not null || !string.IsNullOrWhiteSpace(binCode)))
        {
            if (binId is not null && !string.IsNullOrWhiteSpace(binCode)) { errors[key + ".damageBinId"] = new[] { ReceivingModeRules.TargetIdAndCode }; return null; }
            var trimmed = binCode?.Trim() ?? string.Empty;
            var upper = trimmed.ToUpperInvariant();
            var found = binId is int id
                ? await TargetBinsQuery(warehouseId, b => b.WarehouseBinId == id).FirstOrDefaultAsync(ct)
                : await TargetBinsQuery(warehouseId, b => b.Code == trimmed || b.Code == upper).FirstOrDefaultAsync(ct);
            if (found is null)
            {
                if (binId is not null) throw new NotFoundException("Posición", feminine: true);
                errors[key + ".damageBinCode"] = new[] { ReceivingModeRules.TargetCodeNotFound(trimmed) };
                return null;
            }
            if (!found.IsActive) throw new StatusRuleException(ReceivingModeRules.TargetBinInactive(found.Code));
            if (!found.ZoneActive) throw new StatusRuleException(ReceivingModeRules.TargetZoneInactive(found.Code));
            resolvedBin = found.BinId;
        }
        return new LineDamage(damaged.Value, causeId, notes, resolvedBin, discard == true);
    }

    /// <summary>
    /// Lote 16 — posición destino de una línea, por id o por código (no ambos → 400 en idField), SIEMPRE del almacén del
    /// recibo: id de otro almacén o tenant → 404 'Posición no encontrada.'; código inexistente → 400 en codeField; zona
    /// STAGING o CROSSDOCK → 400 (D5); posición o zona desactivada → 422. Los 400 van a errors (y devuelve null); sin id ni
    /// código devuelve null sin error.
    /// </summary>
    private async Task<TargetBinInfo?> ResolveTargetBinAsync(int warehouseId, int? id, string? code, string idField, string codeField,
        Dictionary<string, string[]> errors, CancellationToken ct)
    {
        var hasCode = !string.IsNullOrWhiteSpace(code);
        if (id is null && !hasCode) return null;
        if (id is not null && hasCode) { errors[idField] = new[] { ReceivingModeRules.TargetIdAndCode }; return null; }
        var trimmed = code?.Trim() ?? string.Empty;
        var upper = trimmed.ToUpperInvariant();
        var found = id is int binId
            ? await TargetBinsQuery(warehouseId, b => b.WarehouseBinId == binId).FirstOrDefaultAsync(ct)
            : await TargetBinsQuery(warehouseId, b => b.Code == trimmed || b.Code == upper).FirstOrDefaultAsync(ct);
        if (found is null)
        {
            if (id is not null) throw new NotFoundException("Posición", feminine: true);
            errors[codeField] = new[] { ReceivingModeRules.TargetCodeNotFound(trimmed) };
            return null;
        }
        var target = found with { ZoneTypeCode = await ZoneTypeCodeAsync(found.ZoneTypeLookupId, ct) };
        var zoneError = ReceivingModeRules.ValidateTargetZone(target.Code, target.ZoneTypeCode);
        if (zoneError is not null) { errors[id is not null ? idField : codeField] = new[] { zoneError }; return null; }
        if (!target.IsActive) throw new StatusRuleException(ReceivingModeRules.TargetBinInactive(target.Code));
        if (!target.ZoneActive) throw new StatusRuleException(ReceivingModeRules.TargetZoneInactive(target.Code));
        return target;
    }

    /// <summary>Lote 16: posición destino con su zona (tipo, activa) y su cupo.</summary>
    private sealed record TargetBinInfo(int BinId, string Code, bool IsActive, bool ZoneActive, int? ZoneTypeLookupId, int? MaxCapacityQty,
        string? ZoneTypeCode = null);

    /// <summary>
    /// Posiciones del almacén (hijas sin TenantId, alcanzadas por su almacén filtrado) con su zona. El filtro va sobre la
    /// posición ANTES de proyectar (EF no traduce un Where sobre un record construido por su constructor).
    /// </summary>
    private IQueryable<TargetBinInfo> TargetBinsQuery(int warehouseId, System.Linq.Expressions.Expression<Func<WarehouseBin, bool>> filter)
        => from b in db.Set<WarehouseBin>().AsNoTracking().Where(filter)
           join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
           join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
           where b.WarehouseId == warehouseId
           select new TargetBinInfo(b.WarehouseBinId, b.Code, b.IsActive, z.IsActive, z.ZoneTypeLookupId, b.MaxCapacityQty, null);

    /// <summary>Lote 16: posiciones destino por id (solo las del almacén), con el código de tipo de zona resuelto.</summary>
    private async Task<Dictionary<int, TargetBinInfo>> TargetBinsAsync(int warehouseId, IEnumerable<int> binIds, CancellationToken ct)
    {
        var ids = binIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, TargetBinInfo>();
        var rows = await TargetBinsQuery(warehouseId, b => ids.Contains(b.WarehouseBinId)).ToListAsync(ct);
        var result = new Dictionary<int, TargetBinInfo>();
        foreach (var b in rows) result[b.BinId] = b with { ZoneTypeCode = await ZoneTypeCodeAsync(b.ZoneTypeLookupId, ct) };
        return result;
    }

    private async Task<string?> ZoneTypeCodeAsync(int? zoneTypeLookupId, CancellationToken ct)
        => zoneTypeLookupId is int zt ? (await lookups.GetAsync(zt, ct))?.InternalCode : null;

    // ---------------------------------------------------------------- Lote 16: modo de recepción

    private async Task<string?> LookupCodeOfAsync(int? lookupId, CancellationToken ct)
        => lookupId is int id ? (await lookups.GetAsync(id, ct))?.InternalCode : null;

    /// <summary>Modo efectivo del recibo: su copia; sin ella (recibos anteriores al lote sin seed), el del almacén; si no, PUTAWAY.</summary>
    private async Task<string> ReceiptModeAsync(ReceiptHeader r, CancellationToken ct)
    {
        var own = await LookupCodeOfAsync(r.ReceivingModeLookupId, ct);
        if (own is not null) return ReceivingModeRules.Normalize(own);
        var warehouseModeId = await db.Set<Warehouse>().AsNoTracking().Where(w => w.WarehouseId == r.WarehouseId)
            .Select(w => w.ReceivingModeLookupId).FirstOrDefaultAsync(ct);
        return ReceivingModeRules.Effective(null, await LookupCodeOfAsync(warehouseModeId, ct));
    }

    /// <summary>Id del catálogo del modo (null si el catálogo no tiene PUTAWAY: NULL en la base = PUTAWAY).</summary>
    private async Task<int?> ModeIdAsync(string mode, CancellationToken ct)
    {
        var id = await lookups.TryGetIdAsync(LookupDomains.ReceivingMode, mode, ct);
        if (id is null && mode == ReceivingModes.Direct)
            throw new InvalidOperationException("El catálogo ReceivingMode no tiene DIRECT (logistica-db-seed.sql).");
        return id;
    }

    /// <summary>Etiqueta del modo en el idioma del usuario (el código si el catálogo no lo tiene).</summary>
    private async Task<string> ModeLabelAsync(string mode, CancellationToken ct)
    {
        var id = await lookups.TryGetIdAsync(LookupDomains.ReceivingMode, mode, ct);
        var l = id is int x ? await lookups.GetAsync(x, ct) : null;
        return l is null ? mode : MultilingualText.Resolve(l.LabelJson, tenant.Lang);
    }

    /// <summary>Posición de recepción para una línea agregada: la de otra línea del recibo o la STAGING por defecto del almacén.</summary>
    private async Task<int> DefaultLineStagingAsync(ReceiptHeader r, CancellationToken ct)
        => await db.Set<ReceiptLine>().AsNoTracking().Where(l => l.ReceiptHeaderId == r.ReceiptHeaderId && l.StagingBinId != null)
               .OrderBy(l => l.ReceiptLineId).Select(l => l.StagingBinId).FirstOrDefaultAsync(ct)
           ?? await ReceivingSupport.DefaultStagingBinAsync(db, r.WarehouseId, ct);

    /// <summary>Lote 16: como DefaultLineStagingAsync, pero null si el almacén no tiene posición de recepción (recibo directo).</summary>
    private async Task<int?> DefaultLineStagingOrNullAsync(ReceiptHeader r, CancellationToken ct)
        => await db.Set<ReceiptLine>().AsNoTracking().Where(l => l.ReceiptHeaderId == r.ReceiptHeaderId && l.StagingBinId != null)
               .OrderBy(l => l.ReceiptLineId).Select(l => l.StagingBinId).FirstOrDefaultAsync(ct)
           ?? await ReceivingSupport.TryDefaultStagingBinAsync(db, r.WarehouseId, ct);

    /// <summary>Cantidad destinada a cruce de muelle por línea (asignaciones no canceladas: la confirmada si ya se repartió, si no la asignada).</summary>
    private async Task<Dictionary<int, decimal>> CrossDockByLineAsync(List<int> lineIds, CancellationToken ct)
    {
        if (lineIds.Count == 0) return new Dictionary<int, decimal>();
        var cancelledId = await db.StatusIdAsync(StatusDomains.AllocationStatus, AllocationStatuses.Cancelled, ct);
        var rows = await db.Set<CrossDockAllocation>().AsNoTracking()
            .Where(a => lineIds.Contains(a.ReceiptLineId) && a.StatusCodeId != cancelledId)
            .Select(a => new { a.ReceiptLineId, Qty = a.ConfirmedQty ?? a.AllocatedQty }).ToListAsync(ct);
        return rows.GroupBy(x => x.ReceiptLineId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
    }

    private static IReadOnlyList<string> ParseSerials(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json, Json) ?? new List<string>(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    private static List<string> Codes(IEnumerable<string> values)
        => values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim().ToUpperInvariant()).Distinct().ToList();

    /// <summary>SaveChanges con 409: UX_Receipt_Asn → AsnBusy; otro índice único (UQ_Receipt_Number) → NumberTaken; RowVersion → concurrencia.</summary>
    private async Task SaveReceiptAsync(CancellationToken ct)
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
            var asnBusy = false;
            for (Exception? e = ex; e is not null; e = e.InnerException)
                if (e.Message.Contains("UX_Receipt_Asn", StringComparison.OrdinalIgnoreCase)) asnBusy = true;
            throw new ConflictException(asnBusy ? ReceiptRules.AsnBusy : NumberTakenMessage);
        }
    }

    // ---------------------------------------------------------------- encabezados de lista (sin N+1)

    private async Task<Dictionary<int, ReceiptListItemDto>> HeadersAsync(List<ReceiptHeader> rows, CancellationToken ct)
    {
        var result = new Dictionary<int, ReceiptListItemDto>();
        if (rows.Count == 0) return result;
        var ids = rows.Select(r => r.ReceiptHeaderId).ToList();
        var whIds = rows.Select(r => r.WarehouseId).Distinct().ToList();
        var asnIds = rows.Where(r => r.AsnId != null).Select(r => r.AsnId!.Value).Distinct().ToList();
        var dockIds = rows.Where(r => r.DockId != null).Select(r => r.DockId!.Value).Distinct().ToList();

        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.WarehouseId))
            .Select(w => new { w.WarehouseId, w.PublicId, w.Code, w.ReceivingModeLookupId }).ToDictionaryAsync(w => w.WarehouseId, ct);
        var docks = await db.Set<WarehouseDock>().AsNoTracking().Where(d => dockIds.Contains(d.WarehouseDockId) && whIds.Contains(d.WarehouseId))
            .ToDictionaryAsync(d => d.WarehouseDockId, d => d.Code, ct);
        var asnsInfo = await (from a in db.Set<Asn>().AsNoTracking()
                              where asnIds.Contains(a.AsnId)
                              join c in db.Set<Client>().AsNoTracking() on a.ClientId equals c.ClientId into cj
                              from c in cj.DefaultIfEmpty()
                              join p in db.Set<PurchaseOrder>().AsNoTracking() on a.PurchaseOrderId equals p.PurchaseOrderId into pj
                              from p in pj.DefaultIfEmpty()
                              join s in db.Set<Supplier>().AsNoTracking() on p.SupplierId equals s.SupplierId into sj
                              from s in sj.DefaultIfEmpty()
                              select new
                              {
                                  a.AsnId, a.Reference, a.PurchaseOrderId,
                                  ClientName = c == null ? null : c.Name,
                                  PoNumber = p == null ? null : p.Number,
                                  SupplierName = s == null ? null : s.Name,
                                  // Lote 13: llegada esperada = la del aviso; si no, la de la orden de compra.
                                  ExpectedDate = a.ExpectedDate ?? (p == null ? null : p.ExpectedDate),
                              }).ToDictionaryAsync(x => x.AsnId, ct);
        var lineAgg = await db.Set<ReceiptLine>().AsNoTracking().Where(l => ids.Contains(l.ReceiptHeaderId))
            .Select(l => new { l.ReceiptHeaderId, l.ExpectedQty, l.ReceivedQty }).ToListAsync(ct);
        var statusMap = await ReceivingSupport.StatusMapAsync(db, tenant, StatusDomains.ReceiptStatus, ct);
        // Lote 13: posición de recepción por defecto (del almacén del recibo) y tareas de acomodo abiertas por recibo.
        var stagingIds = rows.Where(r => r.DefaultStagingBinId != null).Select(r => r.DefaultStagingBinId!.Value).Distinct().ToList();
        var stagingBins = stagingIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Set<WarehouseBin>().AsNoTracking().Where(b => stagingIds.Contains(b.WarehouseBinId) && whIds.Contains(b.WarehouseId))
                .ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code, ct);
        var pendingPutaway = await PendingPutawayCountsAsync(ids, ct);
        // Lote 16: etiqueta del modo por código (dos a lo sumo), resuelta una vez para la página.
        var modeLabels = new Dictionary<string, string>();

        foreach (var r in rows)
        {
            var type = await lookups.GetAsync(r.ReceiptTypeLookupId, ct);
            var typeCode = type?.InternalCode ?? ReceiptTypes.Blind;
            var expects = ReceiptRules.ExpectsQuantities(typeCode);
            var a = r.AsnId is int aid ? asnsInfo.GetValueOrDefault(aid) : null;
            var origin = ReceiptRules.OriginOf(typeCode, a is not null, a?.PurchaseOrderId is not null);
            var originRef = a is null ? null : (a.PurchaseOrderId is not null ? a.PoNumber : a.Reference);
            var sender = a is null ? null : (a.PurchaseOrderId is not null ? a.SupplierName : a.ClientName);
            var lines = lineAgg.Where(l => l.ReceiptHeaderId == r.ReceiptHeaderId).ToList();
            var expected = lines.Sum(l => ReceiptRules.ExpectedFor(expects, l.ExpectedQty, l.ReceivedQty));
            var received = lines.Sum(l => l.ReceivedQty);
            var hasVariance = lines.Any(l => ReceiptRules.HasVariance(expects, l.ExpectedQty, l.ReceivedQty));
            var w = warehouses.GetValueOrDefault(r.WarehouseId);
            var s = statusMap.GetValueOrDefault(r.StatusCodeId);
            var mode = ReceivingModeRules.Effective(await LookupCodeOfAsync(r.ReceivingModeLookupId, ct),
                await LookupCodeOfAsync(w?.ReceivingModeLookupId, ct));
            if (!modeLabels.TryGetValue(mode, out var modeLabel)) modeLabels[mode] = modeLabel = await ModeLabelAsync(mode, ct);
            result[r.ReceiptHeaderId] = new ReceiptListItemDto(r.ReceiptHeaderId, r.PublicId, r.Number, typeCode,
                type is null ? typeCode : MultilingualText.Resolve(type.LabelJson, tenant.Lang), origin, originRef, sender,
                w?.PublicId ?? Guid.Empty, w?.Code ?? "", r.DockId is int d ? docks.GetValueOrDefault(d) : null,
                s?.Code ?? "", s?.Label ?? "", lines.Count, expected, received, received - expected, hasVariance,
                r.CreatedAtUtc, r.ReceivedAtUtc,
                r.Carrier, r.Reference, a?.ExpectedDate, r.DefaultStagingBinId,
                r.DefaultStagingBinId is int sb ? stagingBins.GetValueOrDefault(sb) : null, r.DockId,
                ReceiptStatusRules.IsOpen(s?.Code), pendingPutaway.GetValueOrDefault(r.ReceiptHeaderId), null, mode, modeLabel);
        }
        return result;
    }

    /// <summary>Líneas armadas por recibo y cantidad a cruce de muelle por línea (solo líneas que tienen asignaciones).</summary>
    private sealed record LineBatch(Dictionary<int, List<ReceiptLineDto>> ByReceipt, Dictionary<int, decimal> CrossDock);

    /// <summary>
    /// Líneas de los recibos dados (la ficha: uno; la lista con includeLines: la página completa) con producto, rastreo, lote,
    /// series, posición de recepción, costo de la orden de compra y cruce de muelle, en un número fijo de consultas sin
    /// importar cuántos recibos o líneas haya. Los recibos ya vienen filtrados por tenant; las líneas (sin TenantId) se leen
    /// SOLO por esos recibos, y la posición y el costo se aceptan solo si son del almacén y del aviso de su recibo.
    /// </summary>
    private async Task<LineBatch> LinesAsync(List<ReceiptHeader> rows, IReadOnlyDictionary<int, ReceiptListItemDto> headers, CancellationToken ct)
    {
        var byReceipt = new Dictionary<int, List<ReceiptLineDto>>();
        if (rows.Count == 0) return new LineBatch(byReceipt, new Dictionary<int, decimal>());
        var receiptIds = rows.Select(r => r.ReceiptHeaderId).ToList();
        var whIds = rows.Select(r => r.WarehouseId).Distinct().ToList();
        var asnIds = rows.Where(r => r.AsnId != null).Select(r => r.AsnId!.Value).Distinct().ToList();

        var lines = await (from l in db.Set<ReceiptLine>().AsNoTracking()
                           join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                           where receiptIds.Contains(l.ReceiptHeaderId)
                           orderby l.ReceiptHeaderId, l.ReceiptLineId
                           select new { Line = l, p.PublicId, p.Sku, p.Name, p.TrackingTypeLookupId }).ToListAsync(ct);
        var lineIds = lines.Select(x => x.Line.ReceiptLineId).ToList();
        var tracking = await ReceivingSupport.TrackingCodesAsync(db, lines.Select(x => x.TrackingTypeLookupId), ct);

        var lotIds = lines.Where(x => x.Line.LotId != null).Select(x => x.Line.LotId!.Value).Distinct().ToList();
        var lots = lotIds.Count == 0
            ? new Dictionary<int, (string LotNumber, DateOnly? ExpiryDate)>()
            : (await (from lot in db.Set<InventoryLot>().AsNoTracking()
                      join p in db.Set<Product>().AsNoTracking() on lot.ProductId equals p.ProductId
                      where lotIds.Contains(lot.LotId)
                      select new { lot.LotId, lot.LotNumber, lot.ExpiryDate }).ToListAsync(ct))
                .ToDictionary(x => x.LotId, x => (x.LotNumber, x.ExpiryDate));
        // Lote 16: también las posiciones destino (con tipo de zona y cupo).
        var binIds = lines.SelectMany(x => new[] { x.Line.StagingBinId, x.Line.TargetBinId, x.Line.DamageBinId }).Where(b => b != null).Select(b => b!.Value).Distinct().ToList();
        var binRows = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                     join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                     where whIds.Contains(b.WarehouseId) && binIds.Contains(b.WarehouseBinId)
                     select new { b.WarehouseBinId, b.WarehouseId, b.Code, b.MaxCapacityQty, z.ZoneTypeLookupId }).ToListAsync(ct);
        var bins = binRows.ToDictionary(b => b.WarehouseBinId, b => (b.WarehouseId, b.Code));
        var targetInfo = new Dictionary<int, (string? ZoneTypeCode, int? MaxCapacityQty)>();
        foreach (var b in binRows) targetInfo[b.WarehouseBinId] = (await ZoneTypeCodeAsync(b.ZoneTypeLookupId, ct), b.MaxCapacityQty);
        // Espacio libre del destino (solo recibos abiertos y destinos con cupo): existencia de la posición (todos los productos)
        // y lo recibido por línea hacia ella en cada recibo (para descontar lo de las OTRAS líneas).
        var openIds = rows.Where(r => headers[r.ReceiptHeaderId].IsOpen).Select(r => r.ReceiptHeaderId).ToHashSet();
        var capTargets = lines.Where(x => openIds.Contains(x.Line.ReceiptHeaderId) && x.Line.TargetBinId is int t
                                          && targetInfo.TryGetValue(t, out var ti) && ti.MaxCapacityQty is not null)
            .Select(x => x.Line.TargetBinId!.Value).Distinct().ToList();
        var binOnHand = capTargets.Count == 0
            ? new Dictionary<int, decimal>()
            : (await db.Set<StockBalance>().AsNoTracking()
                .Where(sb => whIds.Contains(sb.WarehouseId) && sb.WarehouseBinId != null && capTargets.Contains(sb.WarehouseBinId.Value))
                .Select(sb => new { Bin = sb.WarehouseBinId!.Value, sb.QtyOnHand }).ToListAsync(ct))
                .GroupBy(x => x.Bin).ToDictionary(g => g.Key, g => g.Sum(x => x.QtyOnHand));
        var receiptBinQty = lines.Where(x => x.Line.TargetBinId != null)
            .GroupBy(x => (x.Line.ReceiptHeaderId, Bin: x.Line.TargetBinId!.Value))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Line.ReceivedQty));
        var asnLineIds = lines.Where(x => x.Line.AsnLineId != null).Select(x => x.Line.AsnLineId!.Value).ToList();
        var unitCosts = asnIds.Count == 0 || asnLineIds.Count == 0
            ? new Dictionary<int, (int AsnId, decimal UnitCost)>()
            : (await (from al in db.Set<AsnLine>().AsNoTracking()
                      join pl in db.Set<PurchaseOrderLine>().AsNoTracking() on al.PurchaseOrderLineId equals pl.PurchaseOrderLineId
                      where asnIds.Contains(al.AsnId) && asnLineIds.Contains(al.AsnLineId)
                      select new { al.AsnLineId, al.AsnId, pl.UnitCost }).ToListAsync(ct))
                .ToDictionary(x => x.AsnLineId, x => (x.AsnId, x.UnitCost));
        var crossDock = await CrossDockByLineAsync(lineIds, ct);

        var receipts = rows.ToDictionary(r => r.ReceiptHeaderId);
        foreach (var x in lines)
        {
            var l = x.Line;
            var r = receipts[l.ReceiptHeaderId];
            var expects = ReceiptRules.ExpectsQuantities(headers[l.ReceiptHeaderId].TypeCode);
            var lot = l.LotId is int lid && lots.TryGetValue(lid, out var lv) ? lv : ((string LotNumber, DateOnly? ExpiryDate)?)null;
            var binCode = l.StagingBinId is int b && bins.TryGetValue(b, out var bin) && bin.WarehouseId == r.WarehouseId ? bin.Code : null;
            // Lote 16: posición destino (del almacén del recibo) y su espacio libre si el recibo está abierto.
            int? targetId = null;
            string? targetCode = null, targetZoneType = null;
            decimal? targetFree = null;
            if (l.TargetBinId is int tb && bins.TryGetValue(tb, out var tbin) && tbin.WarehouseId == r.WarehouseId)
            {
                targetId = tb;
                targetCode = tbin.Code;
                var info = targetInfo.GetValueOrDefault(tb);
                targetZoneType = info.ZoneTypeCode;
                if (openIds.Contains(r.ReceiptHeaderId) && info.MaxCapacityQty is not null)
                    targetFree = ReceivingModeRules.FreeQty(info.MaxCapacityQty, binOnHand.GetValueOrDefault(tb),
                        receiptBinQty.GetValueOrDefault((r.ReceiptHeaderId, tb)) - l.ReceivedQty);
            }
            decimal? unitCost = l.AsnLineId is int al && unitCosts.TryGetValue(al, out var cost) && cost.AsnId == r.AsnId ? cost.UnitCost : null;
            var dto = new ReceiptLineDto(l.ReceiptLineId, l.AsnLineId, x.PublicId, x.Sku, x.Name,
                tracking.GetValueOrDefault(x.TrackingTypeLookupId, TrackingTypes.None), l.ExpectedQty, l.ReceivedQty,
                ReceiptRules.LineVariance(expects, l.ExpectedQty, l.ReceivedQty), l.LotId, lot?.LotNumber, lot?.ExpiryDate,
                ParseSerials(l.SerialNumbersJson), l.StagingBinId, binCode, l.AdjustmentTxnId, unitCost,
                crossDock.GetValueOrDefault(l.ReceiptLineId), targetId, targetCode, targetZoneType, targetFree,
                l.DamagedQty, l.DamageCauseLookupId is int dc ? (await lookups.GetAsync(dc, ct))?.InternalCode : null, l.DamageNote, l.DamageBinId,
                l.DamageBinId is int db0 && bins.TryGetValue(db0, out var dbin) && dbin.WarehouseId == r.WarehouseId ? dbin.Code : null, l.DamageDiscard,
                l.DamageReportId, l.DamageReportId is int dr ? DamageRules.Code(dr) : null);
            if (!byReceipt.TryGetValue(l.ReceiptHeaderId, out var list)) byReceipt[l.ReceiptHeaderId] = list = new List<ReceiptLineDto>();
            list.Add(dto);
        }
        return new LineBatch(byReceipt, crossDock);
    }

    /// <summary>Lote 13: tareas PUTAWAY abiertas (PENDING o IN_PROGRESS) por recibo, en una consulta agrupada.</summary>
    private async Task<Dictionary<int, int>> PendingPutawayCountsAsync(List<int> receiptIds, CancellationToken ct)
    {
        var receiptTypeId = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.Receipt, ct);
        var putawayId = await lookups.TryGetIdAsync(LookupDomains.WarehouseTaskType, WarehouseTaskTypes.Putaway, ct);
        if (receiptTypeId is null || putawayId is null || receiptIds.Count == 0) return new Dictionary<int, int>();
        var openTaskIds = await db.StatusIdsAsync(StatusDomains.WarehouseTaskStatus,
            new[] { WarehouseTaskStatuses.Pending, WarehouseTaskStatuses.InProgress }, ct);
        var refType = receiptTypeId.Value;
        var taskType = putawayId.Value;
        var rows = await db.Set<WarehouseTask>().AsNoTracking()
            .Where(t => t.RefEntityLookupId == refType && t.TaskTypeLookupId == taskType && t.RefId != null
                        && receiptIds.Contains(t.RefId.Value) && openTaskIds.Contains(t.StatusCodeId))
            .GroupBy(t => t.RefId!.Value)
            .Select(g => new { ReceiptId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(x => x.ReceiptId, x => x.Count);
    }

    // ---------------------------------------------------------------- tareas de putaway del recibo

    private async Task<IReadOnlyList<WarehouseTaskDto>> PutawayTasksAsync(ReceiptHeader r, ReceiptListItemDto header, CancellationToken ct)
    {
        var receiptTypeId = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.Receipt, ct);
        var putawayId = await lookups.TryGetIdAsync(LookupDomains.WarehouseTaskType, WarehouseTaskTypes.Putaway, ct);
        if (receiptTypeId is null || putawayId is null) return Array.Empty<WarehouseTaskDto>();
        var tasks = await db.Set<WarehouseTask>().AsNoTracking()
            .Where(t => t.RefEntityLookupId == receiptTypeId && t.RefId == r.ReceiptHeaderId && t.TaskTypeLookupId == putawayId
                        && t.WarehouseId == r.WarehouseId)
            .OrderBy(t => t.WarehouseTaskId).ToListAsync(ct);
        if (tasks.Count == 0) return Array.Empty<WarehouseTaskDto>();

        var productIds = tasks.Where(t => t.ProductId != null).Select(t => t.ProductId!.Value).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId))
            .Select(p => new { p.ProductId, p.PublicId, p.Sku, p.Name }).ToDictionaryAsync(p => p.ProductId, ct);
        var binIds = tasks.SelectMany(t => new[] { t.FromBinId, t.ToBinId }).Where(b => b != null).Select(b => b!.Value).Distinct().ToList();
        var bins = await db.Set<WarehouseBin>().AsNoTracking().Where(b => b.WarehouseId == r.WarehouseId && binIds.Contains(b.WarehouseBinId))
            .ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code, ct);
        var lotIds = tasks.Where(t => t.LotId != null).Select(t => t.LotId!.Value).Distinct().ToList();
        var lots = await db.Set<InventoryLot>().AsNoTracking().Where(l => lotIds.Contains(l.LotId) && productIds.Contains(l.ProductId))
            .ToDictionaryAsync(l => l.LotId, l => l.LotNumber, ct);
        var serialIds = tasks.Where(t => t.SerialId != null).Select(t => t.SerialId!.Value).Distinct().ToList();
        var serials = await db.Set<InventorySerial>().AsNoTracking().Where(s => serialIds.Contains(s.SerialId) && productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.SerialId, s => s.SerialNumber, ct);
        var userIds = tasks.Where(t => t.AssignedToUserId != null).Select(t => t.AssignedToUserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email, ct);
        var statusMap = await ReceivingSupport.StatusMapAsync(db, tenant, StatusDomains.WarehouseTaskStatus, ct);
        var type = await lookups.GetAsync(putawayId.Value, ct);
        var handler = taskHandlers.FirstOrDefault(h => string.Equals(h.TaskType, WarehouseTaskTypes.Putaway, StringComparison.OrdinalIgnoreCase));
        var completable = handler is not null && handler.NotFromQueueMessage is null;

        return tasks.Select(t =>
        {
            var p = t.ProductId is int pid ? products.GetValueOrDefault(pid) : null;
            var s = statusMap.GetValueOrDefault(t.StatusCodeId);
            return new WarehouseTaskDto(t.WarehouseTaskId, WarehouseTaskTypes.Putaway,
                type is null ? WarehouseTaskTypes.Putaway : MultilingualText.Resolve(type.LabelJson, tenant.Lang),
                s?.Code ?? "", s?.Label ?? "", t.Priority, header.WarehousePublicId, header.WarehouseCode,
                p?.PublicId, p?.Sku, p?.Name, t.LotId, t.LotId is int lid ? lots.GetValueOrDefault(lid) : null,
                t.SerialId is int sid ? serials.GetValueOrDefault(sid) : null, t.Quantity,
                t.FromBinId, t.FromBinId is int fb ? bins.GetValueOrDefault(fb) : null,
                t.ToBinId, t.ToBinId is int tb ? bins.GetValueOrDefault(tb) : null,
                EntityTypes.Receipt, r.ReceiptHeaderId, r.Number,
                t.AssignedToUserId, t.AssignedToUserId is int u ? users.GetValueOrDefault(u) : null,
                completable, t.CreatedAtUtc, t.CompletedAtUtc);
        }).ToList();
    }

    // ================================================================ adaptadores a las costuras de P0 (PutawaySuggester, WarehouseTaskWriter)
    // Toda dependencia de las firmas de PutawaySuggester y WarehouseTaskWriter vive aquí.

    /// <summary>Primera posición sugerida para guardar el remanente (null si no hay candidata: el operador la indica al completar).</summary>
    private async Task<int?> SuggestAsync(int warehouseId, int productId, int? lotId, decimal qty, int? stagingBinId, CancellationToken ct)
    {
        var suggestions = await suggester.SuggestAsync(warehouseId, productId, lotId, qty, stagingBinId, 1, ct);
        return suggestions.Count == 0 ? null : suggestions[0].BinId;
    }

    /// <summary>PUTAWAY desde la posición de recepción, con Ref RECEIPT + id (historial null → PENDING lo escribe el writer).</summary>
    private Task CreatePutawayAsync(ReceiptHeader r, ReceiptLine line, decimal qty, int? toBinId, CancellationToken ct)
        => taskWriter.CreateAsync(new WarehouseTaskSpec(
            TaskType: WarehouseTaskTypes.Putaway, WarehouseId: r.WarehouseId, ProductId: line.ProductId, Quantity: qty,
            LotId: line.LotId, FromBinId: line.StagingBinId, ToBinId: toBinId,
            RefEntityType: EntityTypes.Receipt, RefId: r.ReceiptHeaderId), ct);
}
