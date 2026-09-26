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
/// Lote 6 (P4) — avisos de llegada (ASN). Dos orígenes:
/// - ASN de CLIENTE (3PL): lo da de alta el receptor con productos activos cuyo dueño es ese cliente; nace EXPECTED.
/// - ASN de ORDEN DE COMPRA: lo crea la recepción contra PO (CreateForPurchaseOrderAsync, uso interno) por lo pendiente
///   de cada línea que entregó la costura IPurchaseOrderReceiving; lleva PurchaseOrderLineId por línea.
/// Un ASN tiene un solo recibo activo (UX_Receipt_Asn, D6). Al confirmar el recibo pasa a RECEIVED; cancelar solo desde
/// EXPECTED y sin recibo abierto (el recibo se bloquea antes que el ASN en el orden del lote).
/// Lecturas con InventoryScope (D44): con OwnerClientId solo los ASN de ese cliente; los demás dan 404, sin oráculo.
/// El ASN tiene TenantId (filtro global); sus líneas (sin TenantId) se alcanzan SIEMPRE por el ASN filtrado.
/// </summary>
public sealed class AsnService(TeikemDbContext db, ITenantContext tenant, StatusService statuses)
{
    public const string AsnWhat = "Aviso de llegada";

    // ================================================================ lectura

    public async Task<IReadOnlyList<AsnDto>> ListAsync(AsnQuery q, InventoryScope scope, CancellationToken ct)
    {
        q ??= new AsnQuery();
        scope ??= InventoryScope.Any;
        var query = Scoped(scope).AsNoTracking().Where(a => a.IsActive);

        if (q.WarehousePublicId is Guid wh)
            query = query.Where(a => db.Set<Warehouse>().Any(w => w.WarehouseId == a.WarehouseId && w.PublicId == wh));
        if (q.ClientPublicId is Guid cp)
            query = query.Where(a => db.Set<Client>().Any(c => c.ClientId == a.ClientId && c.PublicId == cp));
        if (q.Status is { Length: > 0 })
        {
            var codes = q.Status.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).ToList();
            var ids = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.AsnStatus && codes.Contains(s.InternalCode))
                .Select(s => s.StatusCodeId).ToListAsync(ct);
            query = query.Where(a => ids.Contains(a.StatusCodeId));
        }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(a => (a.Reference != null && a.Reference.Contains(s))
                                     || db.Set<Client>().Any(c => c.ClientId == a.ClientId && c.Name.Contains(s))
                                     || db.Set<PurchaseOrder>().Any(p => p.PurchaseOrderId == a.PurchaseOrderId && p.Number.Contains(s)));
        }

        var rows = await query.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.AsnId)
            .Take(ReceiptRules.MaxPageSize).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<AsnDto> GetAsync(int id, InventoryScope scope, CancellationToken ct)
    {
        var asn = await Scoped(scope ?? InventoryScope.Any).AsNoTracking().FirstOrDefaultAsync(a => a.AsnId == id, ct)
                  ?? throw new NotFoundException(AsnWhat);
        return (await ToDtosAsync(new List<Asn> { asn }, ct))[0];
    }

    // ================================================================ alta (ASN de cliente)

    /// <summary>
    /// Alta de un ASN de cliente: almacén (o el único activo), cliente dueño obligatorio y activo, 1..200 líneas con
    /// productos activos de ESE cliente (400 OwnerMismatch), cantidad esperada &gt; 0 (entera en SERIAL) y lote opcional
    /// solo para productos por lote. Nace EXPECTED con historial ASN.
    /// </summary>
    public async Task<AsnDto> CreateAsync(AsnCreateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var errors = new Dictionary<string, string[]>();

        var warehouse = await ReceivingSupport.ResolveWarehouseOrDefaultAsync(db, req.WarehousePublicId, ct);
        if (!warehouse.IsActive) throw new StatusRuleException(ReceiptRules.WarehouseInactive);

        if (req.ClientPublicId is not Guid clientPublicId) throw new ValidationException("clientPublicId", ReceiptRules.ClientRequired);
        var client = ClientQueries.EnsureClientActive(await db.ResolveClientAsync(clientPublicId, ct));

        var reference = string.IsNullOrWhiteSpace(req.Reference) ? null : req.Reference.Trim();
        if (reference is { Length: > ReceiptRules.ReferenceMaxLength }) errors["reference"] = new[] { ReceiptRules.ReferenceTooLong };

        var lines = req.Lines ?? Array.Empty<AsnLineRequest>();
        if (lines.Count == 0) errors["lines"] = new[] { ReceiptRules.LinesRequired };
        if (lines.Count > ReceiptRules.MaxLines) errors["lines"] = new[] { ReceiptRules.AsnTooManyLines };
        if (errors.Count > 0) throw new ValidationException(errors);

        var products = await ReceivingSupport.ProductsByPublicIdAsync(db, lines.Select(l => l.ProductPublicId), ct);
        var tracking = await ReceivingSupport.TrackingCodesAsync(db, products.Values.Select(p => p.TrackingTypeLookupId), ct);
        var newLines = new List<AsnLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var key = $"lines[{i}]";
            if (l.ProductPublicId is not Guid pid || !products.TryGetValue(pid, out var product))
            {
                if (l.ProductPublicId is null) { errors[key + ".productPublicId"] = new[] { "Indique el producto." }; continue; }
                throw new NotFoundException("Producto");
            }
            if (!product.IsActive) throw new StatusRuleException(ReceiptRules.ProductInactive(product.Sku));
            if (product.ClientId != client.ClientId) { errors[key + ".productPublicId"] = new[] { ReceiptRules.OwnerMismatch(product.Sku) }; continue; }
            var qtyError = ReceiptRules.ValidateExpectedQty(l.ExpectedQty);
            var code = tracking.GetValueOrDefault(product.TrackingTypeLookupId, TrackingTypes.None);
            if (qtyError is null && code == TrackingTypes.Serial && !ReceiptRules.IsInteger(l.ExpectedQty!.Value))
                qtyError = ReceiptRules.SerialIntegerQty(product.Sku);
            if (qtyError is not null) { errors[key + ".expectedQty"] = new[] { qtyError }; continue; }

            string? lotNumber = null;
            if (!string.IsNullOrWhiteSpace(l.LotNumber))
            {
                if (code != TrackingTypes.Lot) { errors[key + ".lotNumber"] = new[] { ReceiptRules.LotNotAllowed(product.Sku) }; continue; }
                var (n, lotError) = ReceiptRules.NormalizeLot(l.LotNumber, null, null);
                if (lotError is not null) { errors[key + ".lotNumber"] = new[] { lotError }; continue; }
                lotNumber = n;
            }
            newLines.Add(new AsnLine { ProductId = product.ProductId, ExpectedQty = l.ExpectedQty!.Value, LotNumber = lotNumber });
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        var initial = await statuses.GetInitialAsync(StatusDomains.AsnStatus, ct);
        var id = await db.RunInTransactionAsync(async ct2 =>
        {
            var asn = new Asn
            {
                TenantId = tenantId, WarehouseId = warehouse.WarehouseId, ClientId = client.ClientId, Reference = reference,
                ExpectedDate = req.ExpectedDate, StatusCodeId = initial.StatusCodeId, IsActive = true,
                CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
            };
            db.Set<Asn>().Add(asn);
            await db.SaveChangesAsync(ct2);
            foreach (var nl in newLines) nl.AsnId = asn.AsnId;
            db.Set<AsnLine>().AddRange(newLines);
            var born = await statuses.TransitionAsync(StatusDomains.AsnStatus, EntityTypes.Asn, asn.AsnId, null, initial.InternalCode, null, ct2);
            asn.StatusCodeId = born.StatusCodeId;
            await db.SaveChangesAsync(ct2);
            return asn.AsnId;
        }, ct);

        return await GetAsync(id, InventoryScope.Any, ct);
    }

    // ================================================================ cancelación

    /// <summary>
    /// Cancela un ASN EXPECTED (entrada lateral sembrada para ASN). Con un recibo abierto sobre él → 409; ya recibido o
    /// cancelado → 422 AsnNotExpected. El ASN se bloquea (UPDLOCK) y se re-verifica dentro de la transacción.
    /// </summary>
    public async Task<AsnDto> CancelAsync(int id, CancellationToken ct)
    {
        var current = await db.Set<Asn>().AsNoTracking().FirstOrDefaultAsync(a => a.AsnId == id && a.IsActive, ct)
                      ?? throw new NotFoundException(AsnWhat);
        var expectedId = await db.StatusIdAsync(StatusDomains.AsnStatus, AsnStatuses.Expected, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var asn = await ReceivingSupport.LockAsnAsync(db, current.AsnId, ct2);
            if (asn.StatusCodeId != expectedId) throw new StatusRuleException(ReceiptRules.AsnNotExpected);
            var openReceipt = await db.Set<ReceiptHeader>().AsNoTracking().AnyAsync(r => r.AsnId == asn.AsnId && r.IsActive, ct2);
            if (openReceipt) throw new ConflictException(ReceiptRules.AsnHasOpenReceipt);
            var to = await statuses.TransitionAsync(StatusDomains.AsnStatus, EntityTypes.Asn, asn.AsnId, asn.StatusCodeId, AsnStatuses.Cancelled, null, ct2);
            asn.StatusCodeId = to.StatusCodeId;
            await db.SaveChangesAsync(ct2);
        }, ct);

        return await GetAsync(id, InventoryScope.Any, ct);
    }

    // ================================================================ uso interno: ASN por lo pendiente de una PO

    /// <summary>
    /// Crea (dentro de la transacción ambiente de la recepción, con la PO ya bloqueada por la costura) un ASN EXPECTED por
    /// lo pendiente de cada línea de la PO, con PurchaseOrderLineId y Reference = número de la PO. Devuelve el ASN tracked
    /// con sus líneas ya guardadas.
    /// </summary>
    public async Task<Asn> CreateForPurchaseOrderAsync(PurchaseOrderForReceipt po, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var pending = po.Lines.Where(l => l.QtyPending > 0m).ToList();
        if (pending.Count == 0) throw new StatusRuleException(PurchaseStatusRules.NothingPending);
        if (pending.Count > ReceiptRules.MaxLines) throw new ValidationException("lines", ReceiptRules.TooManyLines);

        var initial = await statuses.GetInitialAsync(StatusDomains.AsnStatus, ct);
        var asn = new Asn
        {
            TenantId = tenantId, WarehouseId = po.WarehouseId, PurchaseOrderId = po.PurchaseOrderId,
            Reference = po.Number.Length > ReceiptRules.ReferenceMaxLength ? po.Number[..ReceiptRules.ReferenceMaxLength] : po.Number,
            StatusCodeId = initial.StatusCodeId, IsActive = true, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
        };
        db.Set<Asn>().Add(asn);
        await db.SaveChangesAsync(ct);
        var lines = pending.Select(l => new AsnLine
        {
            AsnId = asn.AsnId, ProductId = l.ProductId, ExpectedQty = l.QtyPending, PurchaseOrderLineId = l.PurchaseOrderLineId,
        }).ToList();
        db.Set<AsnLine>().AddRange(lines);
        var born = await statuses.TransitionAsync(StatusDomains.AsnStatus, EntityTypes.Asn, asn.AsnId, null, initial.InternalCode, null, ct);
        asn.StatusCodeId = born.StatusCodeId;
        await db.SaveChangesAsync(ct);
        return asn;
    }

    // ================================================================ helpers

    private IQueryable<Asn> Scoped(InventoryScope scope)
    {
        var q = db.Set<Asn>().AsQueryable();
        if (scope.OwnerClientId is int owner) q = q.Where(a => a.ClientId == owner);
        return q;
    }

    private async Task<IReadOnlyList<AsnDto>> ToDtosAsync(List<Asn> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return Array.Empty<AsnDto>();
        var asnIds = rows.Select(a => a.AsnId).ToList();
        var whIds = rows.Select(a => a.WarehouseId).Distinct().ToList();
        var clientIds = rows.Where(a => a.ClientId != null).Select(a => a.ClientId!.Value).Distinct().ToList();
        var poIds = rows.Where(a => a.PurchaseOrderId != null).Select(a => a.PurchaseOrderId!.Value).Distinct().ToList();

        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.WarehouseId))
            .Select(w => new { w.WarehouseId, w.PublicId, w.Code }).ToDictionaryAsync(w => w.WarehouseId, ct);
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId))
            .Select(c => new { c.ClientId, c.PublicId, c.Name }).ToDictionaryAsync(c => c.ClientId, ct);
        var pos = await db.Set<PurchaseOrder>().AsNoTracking().Where(p => poIds.Contains(p.PurchaseOrderId))
            .Select(p => new { p.PurchaseOrderId, p.PublicId, p.Number }).ToDictionaryAsync(p => p.PurchaseOrderId, ct);
        var lines = await (from l in db.Set<AsnLine>().AsNoTracking()
                           join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                           where asnIds.Contains(l.AsnId)
                           orderby l.AsnLineId
                           select new { l.AsnId, l.AsnLineId, p.PublicId, p.Sku, p.Name, l.ExpectedQty, l.LotNumber, l.PurchaseOrderLineId })
            .ToListAsync(ct);
        var receipts = await db.Set<ReceiptHeader>().AsNoTracking().Where(r => r.AsnId != null && asnIds.Contains(r.AsnId.Value) && r.IsActive)
            .Select(r => new { AsnId = r.AsnId!.Value, r.PublicId, r.Number }).ToListAsync(ct);
        var receiptByAsn = receipts.GroupBy(r => r.AsnId).ToDictionary(g => g.Key, g => g.First());
        var statusMap = await ReceivingSupport.StatusMapAsync(db, tenant, StatusDomains.AsnStatus, ct);

        return rows.Select(a =>
        {
            var w = warehouses.GetValueOrDefault(a.WarehouseId);
            var c = a.ClientId is int cid ? clients.GetValueOrDefault(cid) : null;
            var p = a.PurchaseOrderId is int pid ? pos.GetValueOrDefault(pid) : null;
            var s = statusMap.GetValueOrDefault(a.StatusCodeId);
            var r = receiptByAsn.GetValueOrDefault(a.AsnId);
            var lineDtos = lines.Where(l => l.AsnId == a.AsnId)
                .Select(l => new AsnLineDto(l.AsnLineId, l.PublicId, l.Sku, l.Name, l.ExpectedQty, l.LotNumber, l.PurchaseOrderLineId))
                .ToList();
            return new AsnDto(a.AsnId, w?.PublicId ?? Guid.Empty, w?.Code ?? "", c?.PublicId, c?.Name, p?.PublicId, p?.Number,
                a.Reference, a.ExpectedDate, s?.Code ?? "", s?.Label ?? "", a.IsActive, lineDtos, r?.PublicId, r?.Number);
        }).ToList();
    }
}

/// <summary>
/// Lote 6 (P4) — apoyo compartido por AsnService, ReceiptService y ReceiptDataSource: estatus con etiqueta del tenant,
/// resolución de almacén/producto/posición por su padre filtrado y los adaptadores a las costuras de P0 (InventoryQueries).
/// Toda dependencia de las firmas de InventoryQueries de esta pieza vive aquí.
/// </summary>
internal static class ReceivingSupport
{
    public const string WarehouseRequiredMessage = "Indique el almacén: la compañía tiene más de uno.";
    public const string NoActiveWarehouseMessage = "La compañía no tiene almacenes activos.";

    public sealed record StatusInfo(string Code, string Label, string? Color);

    /// <summary>Estatus del dominio con la etiqueta y el color personalizados del tenant si existen.</summary>
    public static async Task<Dictionary<int, StatusInfo>> StatusMapAsync(TeikemDbContext db, ITenantContext tenant, string domain, CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == domain).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, StatusCodeOverride>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        return codes.ToDictionary(c => c.StatusCodeId, c =>
        {
            var o = overrides.GetValueOrDefault(c.StatusCodeId);
            return new StatusInfo(c.InternalCode, MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, o?.CustomLabelJson), tenant.Lang),
                o?.CustomColorHex ?? c.ColorHex);
        });
    }

    /// <summary>
    /// Almacén por PublicId (404 'Almacén no encontrado.') o, sin él, el único almacén activo del tenant (con más de uno
    /// → 400 'Indique el almacén: la compañía tiene más de uno.'; sin ninguno → 422). Mismos mensajes que WmsResolve.
    /// </summary>
    public static async Task<Warehouse> ResolveWarehouseOrDefaultAsync(TeikemDbContext db, Guid? publicId, CancellationToken ct)
    {
        if (publicId is Guid id)
            return await db.Set<Warehouse>().AsNoTracking().FirstOrDefaultAsync(w => w.PublicId == id, ct)
                   ?? throw new NotFoundException("Almacén");
        var active = await db.Set<Warehouse>().AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.WarehouseId).Take(2).ToListAsync(ct);
        return active.Count switch
        {
            0 => throw new StatusRuleException(NoActiveWarehouseMessage),
            1 => active[0],
            _ => throw new ValidationException("warehousePublicId", WarehouseRequiredMessage),
        };
    }

    /// <summary>Productos del tenant por PublicId (los que no existen simplemente no aparecen).</summary>
    public static async Task<Dictionary<Guid, Product>> ProductsByPublicIdAsync(TeikemDbContext db, IEnumerable<Guid?> publicIds, CancellationToken ct)
    {
        var ids = publicIds.Where(p => p.HasValue).Select(p => p!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, Product>();
        return await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.PublicId)).ToDictionaryAsync(p => p.PublicId, ct);
    }

    /// <summary>Código interno (NONE/LOT/SERIAL) por TrackingTypeLookupId.</summary>
    public static async Task<Dictionary<int, string>> TrackingCodesAsync(TeikemDbContext db, IEnumerable<int> lookupIds, CancellationToken ct)
    {
        var ids = lookupIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();
        return await db.LookupCodes.AsNoTracking().Where(l => ids.Contains(l.LookupCodeId))
            .ToDictionaryAsync(l => l.LookupCodeId, l => l.InternalCode, ct);
    }

    public sealed record StagingBin(int BinId, int WarehouseId, string Code, bool IsActive, string? ZoneTypeCode);

    /// <summary>
    /// Posición del almacén (hija sin TenantId) resuelta SIEMPRE por su almacén filtrado: de otro almacén o de otro tenant
    /// → 404 'Posición no encontrada.'. Incluye el tipo de zona.
    /// </summary>
    public static async Task<StagingBin> ResolveBinAsync(TeikemDbContext db, int warehouseId, int binId, CancellationToken ct)
    {
        var row = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                         join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                         join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                         where b.WarehouseBinId == binId && b.WarehouseId == warehouseId
                         select new { b.WarehouseBinId, b.WarehouseId, b.Code, b.IsActive, z.ZoneTypeLookupId })
                  .FirstOrDefaultAsync(ct)
                  ?? throw new NotFoundException("Posición", feminine: true);
        var zoneType = row.ZoneTypeLookupId is int zt
            ? await db.LookupCodes.AsNoTracking().Where(l => l.LookupCodeId == zt).Select(l => l.InternalCode).FirstOrDefaultAsync(ct)
            : null;
        return new StagingBin(row.WarehouseBinId, row.WarehouseId, row.Code, row.IsActive, zoneType);
    }

    /// <summary>Posición de recepción indicada: del almacén (404), activa (422) y en zona STAGING o CROSSDOCK (400).</summary>
    public static async Task<StagingBin> ResolveStagingBinAsync(TeikemDbContext db, int warehouseId, int binId, string field, CancellationToken ct)
    {
        var bin = await ResolveBinAsync(db, warehouseId, binId, ct);
        if (bin.ZoneTypeCode is not (ZoneTypes.Staging or ZoneTypes.CrossDock)) throw new ValidationException(field, ReceiptRules.StagingMustBeStaging);
        if (!bin.IsActive) throw new StatusRuleException(ReceiptRules.StagingBinInactive);
        return bin;
    }

    /// <summary>Posición de recepción por defecto: la primera activa (por código de zona y de posición) de una zona STAGING activa; si no hay → 422 NoStagingBin.</summary>
    public static async Task<int> DefaultStagingBinAsync(TeikemDbContext db, int warehouseId, CancellationToken ct)
    {
        var stagingIds = await db.LookupCodes.AsNoTracking()
            .Where(l => l.Entity == LookupDomains.ZoneType && l.InternalCode == ZoneTypes.Staging)
            .Select(l => l.LookupCodeId).ToListAsync(ct);
        var binId = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                           join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                           join z in db.Set<WarehouseZone>().AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                           where b.WarehouseId == warehouseId && b.IsActive && z.IsActive
                                 && z.ZoneTypeLookupId != null && stagingIds.Contains(z.ZoneTypeLookupId.Value)
                           orderby z.Code, b.Code
                           select (int?)b.WarehouseBinId).FirstOrDefaultAsync(ct);
        return binId ?? throw new StatusRuleException(ReceiptRules.NoStagingBin);
    }

    // ---------------------------------------------------------------- adaptadores a las costuras de P0 (InventoryQueries)

    /// <summary>Encabezado ReceiptHeader con UPDLOCK, tracked (orden del lote: ReceiptHeader antes que Asn y PurchaseOrder).</summary>
    public static Task<ReceiptHeader> LockReceiptAsync(TeikemDbContext db, int receiptHeaderId, CancellationToken ct)
        => db.LockReceiptAsync(receiptHeaderId, ct);

    /// <summary>Encabezado Asn con UPDLOCK, tracked (después del recibo, antes de la PO).</summary>
    public static Task<Asn> LockAsnAsync(TeikemDbContext db, int asnId, CancellationToken ct)
        => db.LockAsnAsync(asnId, ct);

    /// <summary>
    /// Lote del producto por número (EnsureLot, D34), por LINQ bajo el producto ya resuelto del tenant: si existe se
    /// reutiliza cuando las fechas capturadas coinciden (una fecha no capturada no se compara); con otras fechas → 409
    /// 'El lote {n} ya existe con otras fechas; …' (no se sobrescriben). Si no existe se crea; UQ_Lot (ProductId,
    /// LotNumber) es la última línea ante un alta concurrente (409, reintentar).
    /// </summary>
    public static async Task<int> EnsureLotAsync(TeikemDbContext db, int productId, string lotNumber, DateOnly? manufactureDate, DateOnly? expiryDate, CancellationToken ct)
    {
        var existing = await (from l in db.Set<InventoryLot>().AsNoTracking()
                              join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                              where l.ProductId == productId && l.LotNumber == lotNumber
                              select new { l.LotId, l.LotNumber, l.ManufactureDate, l.ExpiryDate }).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            if (!ReceiptRules.LotDatesMatch(existing.ManufactureDate, existing.ExpiryDate, manufactureDate, expiryDate))
                throw new ConflictException(ReceiptRules.LotExistsWithOtherDates(existing.LotNumber));
            return existing.LotId;
        }
        var lot = new InventoryLot
        {
            ProductId = productId, LotNumber = lotNumber, ManufactureDate = manufactureDate, ExpiryDate = expiryDate, IsActive = true,
        };
        db.Set<InventoryLot>().Add(lot);
        await db.SaveGuardedAsync(ReceiptRules.LotCreatedConcurrently(lotNumber), ct);
        return lot.LotId;
    }
}
