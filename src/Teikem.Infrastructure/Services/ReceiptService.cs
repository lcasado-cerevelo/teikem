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
/// Lote 6 (P4) — Recepción (R7, R8, R9, R10 para crear el putaway, R18). Un recibo nace OPEN con número REC-#####:
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
/// queda congelado (ya está en el ledger): toda edición re-verifica OPEN con el encabezado bloqueado.
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
    IEnumerable<IWarehouseTaskHandler> taskHandlers)
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
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(r => r.Number.Contains(s)
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
        return new ReceiptPageDto(total, skip, take, page.Select(r => items[r.ReceiptHeaderId]).ToList());
    }

    // ================================================================ ficha

    public async Task<ReceiptDetailDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var r = await db.Set<ReceiptHeader>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId && x.IsActive, ct)
                ?? throw new NotFoundException("Recibo");
        var header = (await HeadersAsync(new List<ReceiptHeader> { r }, ct))[r.ReceiptHeaderId];
        var expects = ReceiptRules.ExpectsQuantities(header.TypeCode);

        var lines = await (from l in db.Set<ReceiptLine>().AsNoTracking()
                           join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                           where l.ReceiptHeaderId == r.ReceiptHeaderId
                           orderby l.ReceiptLineId
                           select new { Line = l, p.PublicId, p.Sku, p.Name, p.TrackingTypeLookupId }).ToListAsync(ct);
        var lineIds = lines.Select(x => x.Line.ReceiptLineId).ToList();
        var tracking = await ReceivingSupport.TrackingCodesAsync(db, lines.Select(x => x.TrackingTypeLookupId), ct);

        var lotIds = lines.Where(x => x.Line.LotId != null).Select(x => x.Line.LotId!.Value).Distinct().ToList();
        var lots = await (from lot in db.Set<InventoryLot>().AsNoTracking()
                          join p in db.Set<Product>().AsNoTracking() on lot.ProductId equals p.ProductId
                          where lotIds.Contains(lot.LotId)
                          select new { lot.LotId, lot.LotNumber, lot.ExpiryDate }).ToDictionaryAsync(x => x.LotId, ct);
        var binIds = lines.Where(x => x.Line.StagingBinId != null).Select(x => x.Line.StagingBinId!.Value).Distinct().ToList();
        var bins = await db.Set<WarehouseBin>().AsNoTracking().Where(b => b.WarehouseId == r.WarehouseId && binIds.Contains(b.WarehouseBinId))
            .ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code, ct);
        var asnLineIds = lines.Where(x => x.Line.AsnLineId != null).Select(x => x.Line.AsnLineId!.Value).ToList();
        var unitCosts = r.AsnId is int asnId
            ? await (from al in db.Set<AsnLine>().AsNoTracking()
                     join pl in db.Set<PurchaseOrderLine>().AsNoTracking() on al.PurchaseOrderLineId equals pl.PurchaseOrderLineId
                     where al.AsnId == asnId && asnLineIds.Contains(al.AsnLineId)
                     select new { al.AsnLineId, pl.UnitCost }).ToDictionaryAsync(x => x.AsnLineId, x => x.UnitCost, ct)
            : new Dictionary<int, decimal>();
        var crossDock = await CrossDockByLineAsync(lineIds, ct);

        var lineDtos = lines.Select(x =>
        {
            var l = x.Line;
            var lot = l.LotId is int lid ? lots.GetValueOrDefault(lid) : null;
            return new ReceiptLineDto(l.ReceiptLineId, l.AsnLineId, x.PublicId, x.Sku, x.Name,
                tracking.GetValueOrDefault(x.TrackingTypeLookupId, TrackingTypes.None), l.ExpectedQty, l.ReceivedQty,
                ReceiptRules.LineVariance(expects, l.ExpectedQty, l.ReceivedQty), l.LotId, lot?.LotNumber, lot?.ExpiryDate,
                ParseSerials(l.SerialNumbersJson), l.StagingBinId, l.StagingBinId is int b ? bins.GetValueOrDefault(b) : null,
                l.AdjustmentTxnId, l.AsnLineId is int al && unitCosts.TryGetValue(al, out var cost) ? cost : null,
                crossDock.GetValueOrDefault(l.ReceiptLineId));
        }).ToList();

        var tasks = await PutawayTasksAsync(r, header, ct);
        return new ReceiptDetailDto(header, lineDtos, tasks, Convert.ToBase64String(r.RowVersion ?? Array.Empty<byte>()));
    }

    // ================================================================ alta

    public async Task<ReceiptDetailDto> CreateAsync(ReceiptCreateRequest req, CancellationToken ct)
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

        // Ciego/devolución: almacén indicado o el único activo; el tope de líneas se valida antes de abrir la transacción.
        Warehouse? manualWarehouse = null;
        var requestLines = req.Lines ?? Array.Empty<ReceiptLineRequest>();
        if (typeCode != ReceiptTypes.Asn)
        {
            manualWarehouse = requested ?? await ReceivingSupport.ResolveWarehouseOrDefaultAsync(db, null, ct);
            if (!manualWarehouse.IsActive) throw new StatusRuleException(ReceiptRules.WarehouseInactive);
            if (requestLines.Count == 0) throw new ValidationException("lines", ReceiptRules.LinesRequired);
            if (requestLines.Count > ReceiptRules.MaxLines) throw new ValidationException("lines", ReceiptRules.TooManyLines);
        }

        await numbers.EnsureAsync(NumberKinds.Receipt, null, ct);
        var initial = await statuses.GetInitialAsync(StatusDomains.ReceiptStatus, ct);
        var expectedAsnId = await db.StatusIdAsync(StatusDomains.AsnStatus, AsnStatuses.Expected, ct);

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
                    var built = await BuildLineAsync(warehouse.WarehouseId, requestLines[i], $"lines[{i}]", OwnerRule.Any, null, errors, ct2);
                    if (built is not null) lines.Add(built);
                }
                if (errors.Count > 0) throw new ValidationException(errors);
            }

            // Posición de recepción por defecto (la de la solicitud o la primera STAGING del almacén) para las líneas que no la traen.
            var stagingId = req.StagingBinId is int sb
                ? (await ReceivingSupport.ResolveStagingBinAsync(db, warehouse.WarehouseId, sb, "stagingBinId", ct2)).BinId
                : await ReceivingSupport.DefaultStagingBinAsync(db, warehouse.WarehouseId, ct2);
            foreach (var l in lines) l.StagingBinId ??= stagingId;

            if (req.DockId is int dockId)
            {
                var dockOk = await (from d in db.Set<WarehouseDock>().AsNoTracking()
                                    join w in db.Set<Warehouse>().AsNoTracking() on d.WarehouseId equals w.WarehouseId
                                    where d.WarehouseDockId == dockId && d.WarehouseId == warehouse.WarehouseId
                                    select d.WarehouseDockId).AnyAsync(ct2);
                if (!dockOk) throw new NotFoundException("Muelle");
            }

            // Número REC al final (último bloqueo del orden del lote).
            var seq = await numbers.NextAsync(NumberKinds.Receipt, null, ct2);
            var receipt = new ReceiptHeader
            {
                TenantId = tenantId, WarehouseId = warehouse.WarehouseId, AsnId = asn?.AsnId, DockId = req.DockId,
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
            return receipt.PublicId;
        }, ct);

        return await GetAsync(publicId, ct);
    }

    // ================================================================ edición de líneas (solo OPEN)

    /// <summary>
    /// Captura de una línea del recibo OPEN: cantidad recibida (≥ 0), lote (EnsureLot) o quitarlo, series normalizadas y
    /// posición de recepción. En SERIAL, capturar las series sin cantidad fija la cantidad = número de series. Bajar lo
    /// recibido por debajo de lo asignado a cruce de muelle se permite: el faltante se ve al confirmar (D29).
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
            var trackingCode = await TrackingOfAsync(product, ct2);
            var errors = new Dictionary<string, string[]>();

            var received = line.ReceivedQty;
            if (req.ReceivedQty is not null)
            {
                var e = ReceiptRules.ValidateReceivedQty(req.ReceivedQty);
                if (e is not null) errors["receivedQty"] = new[] { e };
                else received = req.ReceivedQty.Value;
            }

            var serials = ParseSerials(line.SerialNumbersJson);
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

            var lotId = line.LotId;
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
            if (errors.Count > 0) throw new ValidationException(errors);

            line.ReceivedQty = received;
            line.LotId = lotId;
            line.SerialNumbersJson = serials.Count == 0 ? null : JsonSerializer.Serialize(serials, Json);
            await SaveReceiptAsync(ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>
    /// Línea extra (sin línea del aviso) en un recibo OPEN, hasta 200 líneas. En un recibo contra ASN de cliente el producto
    /// debe ser de ese cliente (400 OwnerMismatch); contra PO solo productos propios (400 OwnProductsOnly).
    /// </summary>
    public async Task<ReceiptDetailDto> AddLineAsync(Guid publicId, ReceiptLineRequest req, CancellationToken ct)
    {
        var current = await ResolveAsync(publicId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var r = await LockOpenAsync(current.ReceiptHeaderId, ct2);
            var count = await db.Set<ReceiptLine>().CountAsync(l => l.ReceiptHeaderId == r.ReceiptHeaderId, ct2);
            if (count >= ReceiptRules.MaxLines) throw new ValidationException("lines", ReceiptRules.TooManyLines);

            var owner = OwnerRule.Any;
            int? ownerClientId = null;
            if (r.AsnId is int asnId)
            {
                var asn = await db.Set<Asn>().AsNoTracking().FirstAsync(a => a.AsnId == asnId, ct2);
                if (asn.PurchaseOrderId is not null) owner = OwnerRule.OwnOnly;
                else if (asn.ClientId is int cid) { owner = OwnerRule.Client; ownerClientId = cid; }
            }
            var errors = new Dictionary<string, string[]>();
            var line = await BuildLineAsync(r.WarehouseId, req, "line", owner, ownerClientId, errors, ct2);
            if (errors.Count > 0 || line is null) throw new ValidationException(errors);
            line.ReceiptHeaderId = r.ReceiptHeaderId;
            line.StagingBinId ??= await DefaultLineStagingAsync(r, ct2);
            db.Set<ReceiptLine>().Add(line);
            await SaveReceiptAsync(ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Elimina una línea extra de un recibo OPEN. Las del aviso no se eliminan (409); con cruce de muelle asignado → 409.</summary>
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
            // Línea de un recibo OPEN: aún no hay movimiento que la referencie; se retira del documento.
            db.Set<ReceiptLine>().Remove(line);
            await SaveReceiptAsync(ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ================================================================ confirmación

    public async Task<ReceiptDetailDto> ConfirmAsync(Guid publicId, ReceiptConfirmRequest? req, CancellationToken ct)
    {
        var current = await ResolveAsync(publicId, ct);
        var comment = string.IsNullOrWhiteSpace(req?.Comment) ? null : req!.Comment!.Trim();
        var receiptEntityTypeId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.Receipt, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            // 1-2. Encabezado bloqueado y re-verificación: la segunda de dos confirmaciones simultáneas ve RECEIVED → 422.
            var r = await LockOpenAsync(current.ReceiptHeaderId, ct2);
            db.ApplyRowVersion(r, req?.RowVersion);

            // 3. ASN después del recibo (orden del lote: Receipt < Asn < PO).
            Asn? asn = r.AsnId is int asnId ? await ReceivingSupport.LockAsnAsync(db, asnId, ct2) : null;

            var lines = await db.Set<ReceiptLine>().Where(l => l.ReceiptHeaderId == r.ReceiptHeaderId).OrderBy(l => l.ReceiptLineId).ToListAsync(ct2);
            if (lines.Count == 0) throw new StatusRuleException(ReceiptRules.NoLinesToConfirm);
            var typeCode = (await lookups.GetAsync(r.ReceiptTypeLookupId, ct2))?.InternalCode ?? ReceiptTypes.Blind;
            var expects = ReceiptRules.ExpectsQuantities(typeCode);

            var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
            var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct2);
            var tracking = await ReceivingSupport.TrackingCodesAsync(db, products.Values.Select(p => p.TrackingTypeLookupId), ct2);

            // 4. Seguimiento de cada línea (400 con Errors por línea, sin escribir nada).
            var errors = new Dictionary<string, string[]>();
            var serialsByLine = new Dictionary<int, IReadOnlyList<string>>();
            for (var i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                var p = products[l.ProductId];
                var serials = ParseSerials(l.SerialNumbersJson);
                serialsByLine[l.ReceiptLineId] = serials;
                var te = ReceiptRules.ValidateTracking(tracking.GetValueOrDefault(p.TrackingTypeLookupId, TrackingTypes.None), p.Sku, l.ReceivedQty, l.LotId is not null, serials.Count);
                if (te is not null) errors[$"lines[{i}]"] = new[] { te };
            }
            if (errors.Count > 0) throw new ValidationException(errors);

            // 5. Asientos en MAGNITUD (el ledger pone el signo), To/From la posición de recepción, Ref RECEIPT + id.
            int? defaultStaging = null;
            var postings = new List<InventoryPosting>();
            var owners = new List<(ReceiptLine Line, PlannedReceiptPosting Planned)>();
            foreach (var l in lines)
            {
                var p = products[l.ProductId];
                var code = tracking.GetValueOrDefault(p.TrackingTypeLookupId, TrackingTypes.None);
                // LOT recibido en 0 sin lote: no mueve inventario (la varianza queda visible en la línea).
                if (code == TrackingTypes.Lot && l.LotId is null) continue;
                if (l.StagingBinId is null)
                {
                    defaultStaging ??= await ReceivingSupport.DefaultStagingBinAsync(db, r.WarehouseId, ct2);
                    l.StagingBinId = defaultStaging;
                }
                foreach (var planned in ReceiptPostingRules.Plan(expects, l.ExpectedQty, l.ReceivedQty, code, serialsByLine[l.ReceiptLineId]))
                {
                    postings.Add(new InventoryPosting(planned.TxnType, l.ProductId, planned.Quantity,
                        LotId: l.LotId, SerialNumber: planned.SerialNumber,
                        FromWarehouseId: planned.Inbound ? null : r.WarehouseId, FromBinId: planned.Inbound ? null : l.StagingBinId,
                        ToWarehouseId: planned.Inbound ? r.WarehouseId : null, ToBinId: planned.Inbound ? l.StagingBinId : null,
                        RefEntityType: EntityTypes.Receipt, RefId: r.ReceiptHeaderId, ReasonCode: planned.ReasonCode));
                    owners.Add((l, planned));
                }
            }
            IReadOnlyList<long> txnIds = postings.Count == 0 ? Array.Empty<long>() : await ledger.PostAsync(postings, ct2);

            // 6. AdjustmentTxnId = primer ajuste de varianza de la línea.
            for (var i = 0; i < owners.Count; i++)
                if (owners[i].Planned.TxnType == InventoryTxnTypes.Adjustment && owners[i].Line.AdjustmentTxnId is null)
                    owners[i].Line.AdjustmentTxnId = txnIds[i];

            // 7. PO: lo recibido por línea de PO (las líneas extra no cuentan contra la PO); P8 bloquea la PO y la avanza.
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

            // 8. ASN → RECEIVED.
            if (asn is not null)
            {
                var asnTo = await statuses.TransitionAsync(StatusDomains.AsnStatus, EntityTypes.Asn, asn.AsnId, asn.StatusCodeId, AsnStatuses.Received, null, ct2);
                asn.StatusCodeId = asnTo.StatusCodeId;
            }

            // 9. Recibo OPEN → RECEIVED con fecha de confirmación (insumo de la Contabilización de compras, Lote 10).
            var received = await statuses.TransitionAsync(StatusDomains.ReceiptStatus, EntityTypes.Receipt, r.ReceiptHeaderId, r.StatusCodeId, ReceiptStatuses.Received, comment, ct2);
            r.StatusCodeId = received.StatusCodeId;
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

            // 11. PUTAWAY por el remanente con la posición sugerida (preferida, consolidar, rotación, reserva).
            var created = 0;
            foreach (var l in lines)
            {
                var remaining = l.ReceivedQty - crossDock.GetValueOrDefault(l.ReceiptLineId);
                if (remaining <= 0m) continue;
                var p = products[l.ProductId];
                if (tracking.GetValueOrDefault(p.TrackingTypeLookupId, TrackingTypes.None) == TrackingTypes.Lot && l.LotId is null) continue;
                var toBin = await SuggestAsync(r.WarehouseId, l.ProductId, l.LotId, remaining, l.StagingBinId, ct2);
                await CreatePutawayAsync(r, l, remaining, toBin, ct2);
                created++;
            }

            // 12. Sin ninguna PUTAWAY (todo a cruce de muelle o recibido 0) → RECEIVED → PUTAWAY directo.
            if (created == 0)
            {
                var done = await statuses.TransitionAsync(StatusDomains.ReceiptStatus, EntityTypes.Receipt, r.ReceiptHeaderId, r.StatusCodeId, ReceiptStatuses.Putaway, null, ct2);
                r.StatusCodeId = done.StatusCodeId;
            }
            await SaveReceiptAsync(ct2);
        }, ct);

        return await GetAsync(publicId, ct);
    }

    // ================================================================ baja

    /// <summary>
    /// Elimina (IsActive = 0) un recibo OPEN sin asignaciones de cruce de muelle. Si su ASN nació de una PO, el ASN se
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

    /// <summary>Encabezado bloqueado; activo (404 al segundo de dos borrados) y OPEN (422 ReceiptNotOpen).</summary>
    private async Task<ReceiptHeader> LockOpenAsync(int receiptHeaderId, CancellationToken ct)
    {
        var r = await ReceivingSupport.LockReceiptAsync(db, receiptHeaderId, ct);
        if (!r.IsActive) throw new NotFoundException("Recibo");
        var openId = await db.StatusIdAsync(StatusDomains.ReceiptStatus, ReceiptStatuses.Open, ct);
        if (r.StatusCodeId != openId) throw new StatusRuleException(ReceiptRules.ReceiptNotOpen(r.Number));
        return r;
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

    /// <summary>Línea de la solicitud (ciega, devolución o extra): producto activo, dueño según el origen, cantidad, lote y series.</summary>
    private async Task<ReceiptLine?> BuildLineAsync(int warehouseId, ReceiptLineRequest l, string key, OwnerRule owner, int? ownerClientId,
        Dictionary<string, string[]> errors, CancellationToken ct)
    {
        if (l.ProductPublicId is not Guid pid) { errors[key + ".productPublicId"] = new[] { "Indique el producto." }; return null; }
        var product = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == pid, ct)
                      ?? throw new NotFoundException("Producto");
        // Producto dado de baja: 422 (el estatus del producto no admite recibir), igual que el ledger al asentar.
        if (!product.IsActive) throw new StatusRuleException(ReceiptRules.ProductInactive(product.Sku));
        if (owner == OwnerRule.Client && product.ClientId != ownerClientId) { errors[key + ".productPublicId"] = new[] { ReceiptRules.OwnerMismatch(product.Sku) }; return null; }
        if (owner == OwnerRule.OwnOnly && product.ClientId is not null) { errors[key + ".productPublicId"] = new[] { ReceiptRules.OwnProductsOnly(product.Sku) }; return null; }
        var trackingCode = await TrackingOfAsync(product, ct);

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

        return new ReceiptLine
        {
            ProductId = product.ProductId, LotId = lotId, ReceivedQty = qty.Value, ExpectedQty = null, StagingBinId = stagingId,
            SerialNumbersJson = serials.Count == 0 ? null : JsonSerializer.Serialize(serials, Json),
        };
    }

    /// <summary>Posición de recepción para una línea agregada: la de otra línea del recibo o la STAGING por defecto del almacén.</summary>
    private async Task<int> DefaultLineStagingAsync(ReceiptHeader r, CancellationToken ct)
        => await db.Set<ReceiptLine>().AsNoTracking().Where(l => l.ReceiptHeaderId == r.ReceiptHeaderId && l.StagingBinId != null)
               .OrderBy(l => l.ReceiptLineId).Select(l => l.StagingBinId).FirstOrDefaultAsync(ct)
           ?? await ReceivingSupport.DefaultStagingBinAsync(db, r.WarehouseId, ct);

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
            .Select(w => new { w.WarehouseId, w.PublicId, w.Code }).ToDictionaryAsync(w => w.WarehouseId, ct);
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
                              }).ToDictionaryAsync(x => x.AsnId, ct);
        var lineAgg = await db.Set<ReceiptLine>().AsNoTracking().Where(l => ids.Contains(l.ReceiptHeaderId))
            .Select(l => new { l.ReceiptHeaderId, l.ExpectedQty, l.ReceivedQty }).ToListAsync(ct);
        var statusMap = await ReceivingSupport.StatusMapAsync(db, tenant, StatusDomains.ReceiptStatus, ct);

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
            result[r.ReceiptHeaderId] = new ReceiptListItemDto(r.ReceiptHeaderId, r.PublicId, r.Number, typeCode,
                type is null ? typeCode : MultilingualText.Resolve(type.LabelJson, tenant.Lang), origin, originRef, sender,
                w?.PublicId ?? Guid.Empty, w?.Code ?? "", r.DockId is int d ? docks.GetValueOrDefault(d) : null,
                s?.Code ?? "", s?.Label ?? "", lines.Count, expected, received, received - expected, hasVariance,
                r.CreatedAtUtc, r.ReceivedAtUtc);
        }
        return result;
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
