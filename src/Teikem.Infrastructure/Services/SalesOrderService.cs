using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 57 (P4, S1) — órdenes de venta: el documento que viaja de Ventas a Almacén (OV-#####). Esta parte arma y mantiene la orden en BORRADOR:
/// crear, editar (reemplaza las líneas), consultar y cancelar. Solicitar (validar y reservar), backorders y despacho vienen en los lotes siguientes.
/// - Solo se venden productos propios de la compañía y activos; el precio sale del precio de venta del producto y cambiarlo exige sales.price.
/// - El impuesto de cada línea se calcula con las reglas de impuestos (TaxService) si el módulo TAX está encendido y queda COPIADO en la línea
///   (tasa, porcentaje, monto, exento); el encabezado guarda el subtotal y el impuesto. Sin el módulo TAX el impuesto es cero.
/// - El número OV-##### se toma al final de la transacción (el contador es el último bloqueo). Nunca DELETE de órdenes: se cancelan.
/// </summary>
public sealed partial class SalesOrderService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, INumberSequenceService numbers, PermissionService permissions,
    ModuleService modules, TaxService tax, ITenantClock clock, Teikem.Infrastructure.Wms.InventoryLedger ledger, ILookupCache lookups, IClientBalanceProvider balance,
    ISecurityEventWriter security, Teikem.Infrastructure.Notifications.INotificationPublisher? notifications = null) : ISalesDispatchReversal
{
    public const int MaxPageSize = 200;
    private TenantContext T => (TenantContext)tenant;

    private sealed record PlannedLine(Product Product, decimal Quantity, decimal UnitPrice, int? LotId, IReadOnlyList<(int ProductId, decimal Quantity)>? Components = null, bool CustomComponents = false);

    // ---------------------------------------------------------------- consulta

    public async Task<PagedResult<SalesOrderListItemDto>> ListAsync(string? status, Guid? clientPublicId, string? q, bool backorderOnly, DateOnly? from, DateOnly? to,
        int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, MaxPageSize);
        var map = await StatusMapAsync(ct);
        var query = db.Set<SalesOrder>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var code = status.Trim().ToUpperInvariant();
            var ids = map.Where(kv => kv.Value.Code == code).Select(kv => kv.Key).ToList();
            query = query.Where(o => ids.Contains(o.StatusCodeId));
        }
        if (clientPublicId is Guid cp)
            query = query.Where(o => db.Set<Client>().Any(c => c.ClientId == o.ClientId && c.PublicId == cp));
        if (from is DateOnly f) query = query.Where(o => o.RequestedDate >= f);
        if (to is DateOnly t2) query = query.Where(o => o.RequestedDate <= t2);
        if (backorderOnly) query = query.Where(o => db.Set<SalesOrderLine>().Any(l => l.SalesOrderId == o.SalesOrderId && l.QtyBackordered > 0 && !l.IsClosed));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(o => o.Number.Contains(s)
                || db.Set<Client>().Any(c => c.ClientId == o.ClientId && (c.Name.Contains(s) || c.Code.Contains(s)))
                || db.Set<SalesOrderLine>().Any(l => l.SalesOrderId == o.SalesOrderId && db.Set<Product>().Any(p => p.ProductId == l.ProductId && (p.Sku.Contains(s) || p.Name.Contains(s)))));
        }
        var total = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(o => o.SalesOrderId).Skip(skip).Take(take).ToListAsync(ct);
        var items = await ToListItemsAsync(orders, map, ct);
        return new PagedResult<SalesOrderListItemDto>(items, total, skip, take);
    }

    public async Task<SalesOrderDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var order = await db.Set<SalesOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PublicId == publicId, ct) ?? throw NotFound();
        return (await ToDtosAsync(new[] { order }, ct))[0];
    }

    // ---------------------------------------------------------------- alta

    public async Task<SalesOrderDto> CreateAsync(SalesOrderRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var tenantId = T.RequireTenantId();
        var client = await ResolveClientAsync(req.ClientPublicId, ct);
        var warehouse = await ResolveWarehouseAsync(req.WarehousePublicId, ct);
        var consigneeId = await ResolveConsigneeAsync(req.ConsigneePublicId, ct);
        var (notes, notesError) = SalesRules.NormalizeNotes(req.Notes);
        if (notesError is not null) throw new ValidationException("notes", notesError);
        var planned = await PlanLinesAsync(req.Lines, ct);
        var today = clock.Today;
        var priced = await PriceAsync(planned, client.ClientId, today, ct);

        await numbers.EnsureAsync(SalesCodes.NumberKind, null, ct);
        var id = await db.RunInTransactionAsync(async ct2 =>
        {
            var initial = await statuses.GetInitialAsync(SalesCodes.StatusDomain, ct2);
            var seq = await numbers.NextAsync(SalesCodes.NumberKind, null, ct2);
            var order = new SalesOrder
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, Number = NumberFormat.Resolve(SalesCodes.NumberPattern, seq), WarehouseId = warehouse.WarehouseId,
                ClientId = client.ClientId, ConsigneeId = consigneeId, RequestedBy = tenant.UserId, RequestedDate = today, RequiredDate = req.RequiredDate, Notes = notes,
                StatusCodeId = initial.StatusCodeId,
            };
            ApplyLines(order, priced);
            db.Set<SalesOrder>().Add(order);
            await db.SaveGuardedAsync(SalesRules.NumberTaken, ct2);
            var born = await statuses.TransitionAsync(SalesCodes.StatusDomain, SalesCodes.EntityType, order.SalesOrderId, null, initial.InternalCode, null, ct2);
            order.StatusCodeId = born.StatusCodeId;
            await db.SaveGuardedAsync(SalesRules.NumberTaken, ct2);
            return order.PublicId;
        }, ct);
        return await GetAsync(id, ct);
    }

    // ---------------------------------------------------------------- edición (solo borrador)

    /// <summary>PATCH: lo que no viene no cambia; las líneas, si vienen, REEMPLAZAN a las actuales. Solo en borrador (422 si no).</summary>
    public async Task<SalesOrderDto> UpdateAsync(Guid publicId, SalesOrderRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var (notes, notesError) = SalesRules.NormalizeNotes(req.Notes);
        if (notesError is not null) throw new ValidationException("notes", notesError);
        var peek = await db.Set<SalesOrder>().Where(o => o.PublicId == publicId).Select(o => new { o.StatusCodeId, o.ClientId, o.RequestedDate }).FirstOrDefaultAsync(ct) ?? throw NotFound();
        await EnsureDraftAsync(peek.StatusCodeId, ct);

        var clientId = peek.ClientId;
        if (req.ClientPublicId is not null) clientId = (await ResolveClientAsync(req.ClientPublicId, ct)).ClientId;
        Warehouse? warehouse = req.WarehousePublicId is null ? null : await ResolveWarehouseAsync(req.WarehousePublicId, ct);
        int? consigneeId = req.ConsigneePublicId is null ? null : await ResolveConsigneeAsync(req.ConsigneePublicId, ct);
        var planned = req.Lines is null ? null : await PlanLinesAsync(req.Lines, ct);
        var priced = planned is null ? null : await PriceAsync(planned, clientId, peek.RequestedDate, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.Set<SalesOrder>().AsTracking().Include(o => o.Lines).ThenInclude(l => l.Components).FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
            await EnsureDraftAsync(order.StatusCodeId, ct2);
            order.ClientId = clientId;
            if (warehouse is not null) order.WarehouseId = warehouse.WarehouseId;
            if (req.ClearConsignee == true) order.ConsigneeId = null;
            else if (consigneeId is int c) order.ConsigneeId = c;
            if (req.ClearRequiredDate == true) order.RequiredDate = null;
            else if (req.RequiredDate is DateOnly rd) order.RequiredDate = rd;
            if (req.Notes is not null) order.Notes = notes;
            if (priced is not null)
            {
                // Lote 69: la foto de componentes de un kit se conserva al guardar de nuevo el borrador (incluidas las piezas ajustadas) salvo que el vendedor mande otras
                var kept = order.Lines.Where(l => l.Components.Count > 0).GroupBy(l => l.ProductId)
                    .ToDictionary(g => g.Key, g => (IReadOnlyList<(int ProductId, decimal Quantity)>)g.First().Components.Select(c => (c.ComponentProductId, c.Quantity)).ToList());
                priced = priced.Select(p => p.Line.Components is not null && !p.Line.CustomComponents && kept.TryGetValue(p.Line.Product.ProductId, out var old)
                    ? p with { Line = p.Line with { Components = old } } : p).ToList();
                db.Set<SalesOrderLineComponent>().RemoveRange(order.Lines.SelectMany(l => l.Components));   // la foto de cada línea se va con ella
                db.Set<SalesOrderLine>().RemoveRange(order.Lines);
                order.Lines.Clear();
                ApplyLines(order, priced);
            }
            else if (req.ClientPublicId is not null)
            {
                // cambió el cliente: el impuesto depende de su exención, se recalcula con las mismas líneas
                var again = await PriceAsync(order.Lines.OrderBy(l => l.LineNo).Select(l => new PlannedLine(db.Set<Product>().First(p => p.ProductId == l.ProductId), l.QtyOrdered, l.UnitPrice, l.LotId, l.Components.Count == 0 ? null : l.Components.Select(c => (c.ComponentProductId, c.Quantity)).ToList())).ToList(),
                    clientId, order.RequestedDate, ct2);
                db.Set<SalesOrderLineComponent>().RemoveRange(order.Lines.SelectMany(l => l.Components));
                db.Set<SalesOrderLine>().RemoveRange(order.Lines);
                order.Lines.Clear();
                ApplyLines(order, again);
            }
            await db.SaveGuardedAsync(SalesRules.Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- piezas

    private static NotFoundException NotFound() => new("Orden de venta", feminine: true);

    private async Task EnsureDraftAsync(int statusCodeId, CancellationToken ct)
    {
        if ((await StatusMapAsync(ct))[statusCodeId].Code != SalesOrderStatuses.Draft) throw new StatusRuleException(SalesRules.OnlyDraftEditable);
    }

    private async Task<Client> ResolveClientAsync(Guid? publicId, CancellationToken ct)
    {
        if (publicId is null) throw new ValidationException("clientPublicId", SalesRules.ClientRequired);
        var client = await db.Set<Client>().AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == publicId, ct) ?? throw new NotFoundException("Cliente");
        if (!client.IsActive) throw new StatusRuleException(SalesRules.ClientInactive);
        return client;
    }

    private async Task<int?> ResolveConsigneeAsync(Guid? publicId, CancellationToken ct)
    {
        if (publicId is null) return null;
        var id = await db.Set<Location>().AsNoTracking().Where(l => l.PublicId == publicId && l.IsActive).Select(l => (int?)l.LocationId).FirstOrDefaultAsync(ct);
        return id ?? throw new NotFoundException("Consignatario");
    }

    private async Task<Warehouse> ResolveWarehouseAsync(Guid? publicId, CancellationToken ct)
    {
        if (publicId is Guid id)
        {
            var w = await db.Set<Warehouse>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == id, ct) ?? throw new NotFoundException("Almacén");
            if (!w.IsActive) throw new StatusRuleException(SalesRules.WarehouseInactive);
            return w;
        }
        var active = await db.Set<Warehouse>().AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.WarehouseId).Take(2).ToListAsync(ct);
        if (active.Count == 0) throw new StatusRuleException(SalesRules.NoActiveWarehouse);
        if (active.Count > 1) throw new ValidationException("warehousePublicId", SalesRules.WarehouseRequired);
        return active[0];
    }

    /// <summary>Valida las líneas (producto propio y activo, sin repetir, cantidad, precio) y resuelve el precio por omisión; el cambio de precio exige sales.price.</summary>
    private async Task<List<PlannedLine>> PlanLinesAsync(IReadOnlyList<SalesOrderLineRequest>? lines, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0) throw new ValidationException("lines", SalesRules.LinesRequired);
        if (lines.Count > SalesRules.MaxLines) throw new ValidationException("lines", SalesRules.TooManyLines);
        var ids = lines.Where(l => l.ProductPublicId is not null).Select(l => l.ProductPublicId!.Value).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.PublicId)).ToDictionaryAsync(p => p.PublicId, ct);
        var serialId = await lookups.GetIdAsync(LookupDomains.TrackingType, TrackingTypes.Serial, ct);
        var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);   // Lote 69
        var seen = new HashSet<int>();
        var result = new List<PlannedLine>();
        var canPrice = false;
        var checkedPrice = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.ProductPublicId is null) throw new ValidationException($"lines[{i}].productPublicId", SalesRules.ProductRequired);
            var product = products.GetValueOrDefault(l.ProductPublicId.Value) ?? throw new NotFoundException("Producto");
            if (!product.IsActive) throw new StatusRuleException(SalesRules.ProductInactive);
            if (product.ClientId is not null) throw new StatusRuleException(SalesRules.ProductNotOwn);
            if (product.TrackingTypeLookupId == serialId) throw new StatusRuleException(SalesRules.SerialNotSupported);
            if (!seen.Add(product.ProductId)) throw new ValidationException($"lines[{i}].productPublicId", SalesRules.ProductRepeated);
            var qtyError = SalesRules.QuantityError(l.Quantity);
            if (qtyError is not null) throw new ValidationException($"lines[{i}].quantity", qtyError);
            var priceError = SalesRules.PriceError(l.UnitPrice);
            if (priceError is not null) throw new ValidationException($"lines[{i}].unitPrice", priceError);

            decimal price;
            if (l.UnitPrice is decimal given)
            {
                // escribir un precio distinto del de lista exige el permiso
                if (given != (product.SalePrice ?? -1m))
                {
                    if (!checkedPrice) { canPrice = await permissions.HasPermissionAsync(PermissionCatalog.SalesPrice, ct); checkedPrice = true; }
                    if (!canPrice) throw new ForbiddenException(SalesRules.PriceNeedsPermission);
                }
                price = given;
            }
            else price = product.SalePrice ?? throw new ValidationException($"lines[{i}].unitPrice", SalesRules.NoPrice(product.Sku));

            if (l.LotId is int lotId
                && !await db.Set<InventoryLot>().AsNoTracking().AnyAsync(x => x.LotId == lotId && x.ProductId == product.ProductId, ct))
                throw new NotFoundException("Lote");
            IReadOnlyList<(int ProductId, decimal Quantity)>? components = null;
            if (kitTypeId is not null && product.ProductTypeLookupId == kitTypeId)
            {
                if (l.Quantity!.Value != Math.Floor(l.Quantity.Value)) throw new ValidationException($"lines[{i}].quantity", SalesRules.KitQuantityWhole(product.Sku));
                components = await KitSnapshotAsync(product, l.Components, i, ct);
            }
            else if (l.Components is { Count: > 0 }) throw new ValidationException($"lines[{i}].components", KitRules.NotAKit(product.Sku));
            result.Add(new PlannedLine(product, l.Quantity!.Value, price, l.LotId, components, l.Components is { Count: > 0 }));
        }
        return result;
    }

    /// <summary>
    /// Lote 69 — los componentes que lleva una línea de kit: los del catálogo o, si el kit es «ajustable al pedir» y el vendedor los mandó, los suyos (activos, normales,
    /// propios, sin repetir). Un kit sin componentes no se vende.
    /// </summary>
    private async Task<IReadOnlyList<(int ProductId, decimal Quantity)>> KitSnapshotAsync(Product kit, IReadOnlyList<KitComponentRequest>? custom, int lineIndex, CancellationToken ct)
    {
        if (custom is { Count: > 0 })
        {
            if (!kit.KitAdjustable) throw new ValidationException($"lines[{lineIndex}].components", SalesRules.KitNotAdjustable(kit.Sku));
            if (custom.Count > KitCodes.MaxComponents) throw new ValidationException($"lines[{lineIndex}].components", KitRules.TooManyComponents());
            var ids = custom.Where(c => c.ProductPublicId is not null).Select(c => c.ProductPublicId!.Value).ToList();
            var products = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.PublicId)).ToDictionaryAsync(p => p.PublicId, ct);
            var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
            var seen = new HashSet<Guid>();
            var result = new List<(int, decimal)>();
            for (var j = 0; j < custom.Count; j++)
            {
                var field = $"lines[{lineIndex}].components[{j}]";
                if (custom[j].ProductPublicId is not Guid pid) throw new ValidationException(field, "Indique el producto del componente.");
                if (custom[j].Quantity is not decimal q || q <= 0m) throw new ValidationException(field, KitRules.ComponentQuantity);
                var p = products.GetValueOrDefault(pid) ?? throw new NotFoundException(KitRules.ComponentNotFound);
                if (!seen.Add(pid)) throw new ValidationException(field, KitRules.DuplicateComponent(p.Sku));
                if (p.ProductId == kit.ProductId) throw new ValidationException(field, KitRules.SelfComponent(p.Sku));
                if (p.ProductTypeLookupId == kitTypeId) throw new ValidationException(field, KitRules.ComponentIsKit(p.Sku));
                if (!p.IsActive) throw new ValidationException(field, KitRules.ComponentInactive(p.Sku));
                if (p.ClientId != kit.ClientId) throw new ValidationException(field, KitRules.ComponentOtherOwner(p.Sku));
                result.Add((p.ProductId, q));
            }
            return result;
        }
        var catalog = await db.Set<ProductKitComponent>().AsNoTracking().Where(c => c.KitProductId == kit.ProductId).OrderBy(c => c.SortOrder).Select(c => new { c.ComponentProductId, c.Quantity }).ToListAsync(ct);
        if (catalog.Count == 0) throw new ValidationException($"lines[{lineIndex}].productPublicId", SalesRules.KitWithoutComponents(kit.Sku));
        return catalog.Select(c => (c.ComponentProductId, c.Quantity)).ToList();
    }

    private sealed record PricedLine(PlannedLine Line, decimal Subtotal, decimal TaxAmount, decimal TaxRatePct, int? TaxRateId, bool IsExempt);

    /// <summary>Calcula el impuesto de cada línea (si el módulo TAX está encendido) y devuelve las líneas con su copia.</summary>
    private async Task<List<PricedLine>> PriceAsync(IReadOnlyList<PlannedLine> lines, int clientId, DateOnly date, CancellationToken ct)
    {
        var amounts = lines.Select(l => SalesRules.LineAmount(l.Quantity, l.UnitPrice)).ToList();
        if (!await modules.IsEnabledAsync(ModuleKeys.Tax, ct))
            return lines.Select((l, i) => new PricedLine(l, amounts[i], 0m, 0m, null, false)).ToList();
        var calc = await tax.CalculateAsync(new TaxCalcRequest(clientId, date,
            lines.Select((l, i) => new TaxCalcLineRequest(i.ToString(), l.Product.ProductId, null, amounts[i])).ToList()), ct);
        return lines.Select((l, i) =>
        {
            var r = calc.Lines[i];
            return new PricedLine(l, r.Net, r.TaxAmount, r.TaxRatePct, r.TaxRateId, r.IsTaxExempt);
        }).ToList();
    }

    private void ApplyLines(SalesOrder order, IReadOnlyList<PricedLine> lines)
    {
        var no = 0;
        foreach (var p in lines)
        {
            var line = new SalesOrderLine
            {
                TenantId = order.TenantId, LineNo = ++no, ProductId = p.Line.Product.ProductId, LotId = p.Line.LotId, QtyOrdered = p.Line.Quantity, UnitPrice = p.Line.UnitPrice,
                TaxRateId = p.TaxRateId, TaxRatePct = p.TaxRatePct, TaxAmount = p.TaxAmount, IsTaxExempt = p.IsExempt,
            };
            foreach (var c in p.Line.Components ?? Array.Empty<(int ProductId, decimal Quantity)>())
                line.Components.Add(new SalesOrderLineComponent { TenantId = order.TenantId, ComponentProductId = c.ProductId, Quantity = c.Quantity });
            order.Lines.Add(line);
        }
        order.Subtotal = lines.Sum(l => l.Subtotal);
        order.TaxAmount = lines.Sum(l => l.TaxAmount);
    }

    // ---------------------------------------------------------------- DTOs

    private sealed record StatusInfo(string Code, string Label, string Color);

    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(CancellationToken ct)
        => (await db.StatusCodes.AsNoTracking().Where(s => s.Entity == SalesCodes.StatusDomain).ToListAsync(ct))
            .ToDictionary(s => s.StatusCodeId, s => new StatusInfo(s.InternalCode, MultilingualText.Resolve(s.LabelJson, T.Lang) ?? s.InternalCode, s.ColorHex ?? "#9CA3AF"));

    private async Task<List<SalesOrderListItemDto>> ToListItemsAsync(IReadOnlyList<SalesOrder> orders, Dictionary<int, StatusInfo> map, CancellationToken ct)
    {
        var ids = orders.Select(o => o.SalesOrderId).ToList();
        var lineStats = await db.Set<SalesOrderLine>().Where(l => ids.Contains(l.SalesOrderId)).GroupBy(l => l.SalesOrderId)
            .Select(g => new { Id = g.Key, Count = g.Count(), Back = g.Any(l => l.QtyBackordered > 0 && !l.IsClosed) }).ToDictionaryAsync(x => x.Id, ct);
        var clients = await db.Set<Client>().Where(c => orders.Select(o => o.ClientId).Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, ct);
        var warehouses = await db.Set<Warehouse>().Where(w => orders.Select(o => o.WarehouseId).Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, ct);
        var users = await UserNamesAsync(orders.Where(o => o.RequestedBy is not null).Select(o => o.RequestedBy!.Value), ct);
        return orders.Select(o =>
        {
            var s = map[o.StatusCodeId];
            var c = clients[o.ClientId];
            var w = warehouses[o.WarehouseId];
            var st = lineStats.GetValueOrDefault(o.SalesOrderId);
            return new SalesOrderListItemDto(o.PublicId, o.Number, s.Code, s.Label, s.Color, c.PublicId, c.Name, w.PublicId, w.Name, o.RequestedDate, o.RequiredDate,
                o.Subtotal, o.TaxAmount, o.Subtotal + o.TaxAmount, st?.Count ?? 0, st?.Back ?? false, o.RequestedBy is int u ? users.GetValueOrDefault(u) : null, o.CreatedAtUtc);
        }).ToList();
    }

    private async Task<IReadOnlyList<SalesOrderDto>> ToDtosAsync(IReadOnlyList<SalesOrder> orders, CancellationToken ct)
    {
        var map = await StatusMapAsync(ct);
        var productIds = orders.SelectMany(o => o.Lines.Select(l => l.ProductId)).Distinct().ToList();
        var products = await db.Set<Product>().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        var clientIds = orders.Select(o => o.ClientId).ToList();
        var clients = await db.Set<Client>().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, ct);
        var whIds = orders.Select(o => o.WarehouseId).ToList();
        var warehouses = await db.Set<Warehouse>().Where(w => whIds.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, ct);
        var consIds = orders.Where(o => o.ConsigneeId is not null).Select(o => o.ConsigneeId!.Value).ToList();
        var consignees = await db.Set<Location>().Where(l => consIds.Contains(l.LocationId)).ToDictionaryAsync(l => l.LocationId, ct);
        var users = await UserNamesAsync(orders.Where(o => o.RequestedBy is not null).Select(o => o.RequestedBy!.Value), ct);
        var dispatchInfo = await DispatchesOfAsync(orders.Select(o => o.SalesOrderId).ToList(), ct);
        // Lote 69: kits — la foto de componentes de cada línea y lo reservado de cada componente
        var allLineIds = orders.SelectMany(o => o.Lines.Select(l => l.SalesOrderLineId)).ToList();
        var snapshots = (await db.Set<SalesOrderLineComponent>().AsNoTracking().Where(c => allLineIds.Contains(c.SalesOrderLineId)).ToListAsync(ct))
            .GroupBy(c => c.SalesOrderLineId).ToDictionary(g => g.Key, g => g.ToList());
        var componentProducts = new Dictionary<int, Product>();
        var reservedBy = new Dictionary<(int LineId, int ProductId), decimal>();
        if (snapshots.Count > 0)
        {
            var cids = snapshots.Values.SelectMany(v => v.Select(c => c.ComponentProductId)).Distinct().ToList();
            componentProducts = await db.Set<Product>().Where(p => cids.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
            var kitLineIds = snapshots.Keys.ToList();
            reservedBy = (await db.Set<SalesOrderReservation>().AsNoTracking().Where(r => kitLineIds.Contains(r.SalesOrderLineId) && r.ProductId != null).ToListAsync(ct))
                .GroupBy(r => (r.SalesOrderLineId, r.ProductId!.Value)).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
        }
        return orders.Select(o =>
        {
            var s = map[o.StatusCodeId];
            var c = clients[o.ClientId];
            var w = warehouses[o.WarehouseId];
            var cons = o.ConsigneeId is int ci ? consignees.GetValueOrDefault(ci) : null;
            var lines = o.Lines.OrderBy(l => l.LineNo).Select(l =>
            {
                var p = products[l.ProductId];
                var snap = snapshots.GetValueOrDefault(l.SalesOrderLineId);
                var comps = snap?.Select(c =>
                {
                    var cp = componentProducts[c.ComponentProductId];
                    return new SalesOrderLineComponentDto(cp.PublicId, cp.Sku, cp.Name, c.Quantity, c.Quantity * l.QtyOrdered, reservedBy.GetValueOrDefault((l.SalesOrderLineId, c.ComponentProductId)),
                        c.Quantity * l.QtyBackordered, c.Quantity * l.QtyShipped);
                }).ToList();
                return new SalesOrderLineDto(l.SalesOrderLineId, l.LineNo, p.PublicId, p.Sku, p.Name, l.LotId, l.QtyOrdered, l.QtyReserved, l.QtyBackordered, l.QtyShipped, l.IsClosed,
                    l.UnitPrice, SalesRules.LineAmount(l.QtyOrdered, l.UnitPrice), l.TaxRatePct, l.TaxAmount, l.IsTaxExempt, comps is { Count: > 0 }, comps);
            }).ToList();
            return new SalesOrderDto(o.PublicId, o.Number, s.Code, s.Label, s.Color, c.PublicId, c.Name, cons?.PublicId, cons?.Name, w.PublicId, w.Name, o.RequestedDate, o.RequiredDate,
                o.Notes, o.Subtotal, o.TaxAmount, o.Subtotal + o.TaxAmount, o.Lines.Any(l => SalesRules.IsBackordered(l.QtyBackordered, l.IsClosed)),
                o.RequestedBy is int u ? users.GetValueOrDefault(u) : null, o.RequestedAtUtc, o.CreatedAtUtc, lines, dispatchInfo.GetValueOrDefault(o.SalesOrderId) ?? new List<SalesDispatchInfoDto>());
        }).ToList();
    }

    private async Task<Dictionary<int, string>> UserNamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
    }
}
