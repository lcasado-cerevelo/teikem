using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Constants;
using Teikem.Domain.Contacts;
using Teikem.Domain.ExitNotices;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Búsqueda global («Buscar o ejecutar», 2026-10-21; docs/busqueda-global-dictado-2026-10-20.md): encuentra registros por sus números y nombres en varias
/// pantallas con UNA llamada. Rápida a propósito: números por PREFIJO (`LIKE 'x%'`, que usa los índices) y texto dentro del nombre solo en tablas chicas
/// (clientes, almacenes, productos, proveedores); cada tipo trae pocas filas (<see cref="PerType"/>). Cada tipo se busca solo si el usuario tiene su permiso
/// de lectura y el módulo está encendido (igual que su pantalla). El teléfono se busca por sus dígitos (se guardan sin formato) y lleva al dueño.
/// El servidor solo dice QUÉ encontró (tipo, id, títulos, por qué campo, si fue exacto); a dónde lleva cada tipo lo decide la web.
/// </summary>
public sealed class GlobalSearchService(TeikemDbContext db, ITenantContext tenant, PermissionService permissions, ModuleService modules)
{
    public const int MinLength = 2;
    public const int MaxLength = 64;
    public const int PerType = 6;
    public const int Total = 40;

    // Tipos de resultado (la web los traduce a pantallas y títulos de grupo).
    public const string TypeOrder = "ORDER", TypeTrip = "TRIP", TypeClaim = "CLAIM", TypeClient = "CLIENT", TypeConsignee = "CONSIGNEE",
        TypeWarehouse = "WAREHOUSE", TypeZone = "ZONE", TypeBin = "BIN", TypeProduct = "PRODUCT", TypeSupplier = "SUPPLIER", TypeReceipt = "RECEIPT",
        TypePickBatch = "PICK_BATCH", TypeExitNotice = "EXIT_NOTICE", TypePurchaseOrder = "PURCHASE_ORDER", TypeSalesOrder = "SALES_ORDER",
        TypeInvoice = "INVOICE", TypeRental = "RENTAL";

    private sealed record Access(IReadOnlySet<string> Permissions, IReadOnlySet<string> Modules, bool IsPlatformAdmin)
    {
        public bool Can(string permission, string module) => (IsPlatformAdmin || Permissions.Contains(permission)) && Modules.Contains(module);
        public bool HasPermission(string permission) => IsPlatformAdmin || Permissions.Contains(permission);
    }

    public async Task<SearchResultDto> SearchAsync(string? q, CancellationToken ct)
    {
        var query = (q ?? string.Empty).Trim();
        if (query.Length > MaxLength) query = query[..MaxLength];
        if (query.Length < MinLength) return new SearchResultDto(query, Array.Empty<SearchHitDto>());

        var access = await AccessAsync(ct);
        var hits = new List<SearchHitDto>();
        if (access.Can(PermissionCatalog.OrdersView, ModuleKeys.LtlGround)) hits.AddRange(await OrdersAsync(query, ct));
        if (access.Can(PermissionCatalog.TripsView, ModuleKeys.LtlGround)) hits.AddRange(await TripsAsync(query, ct));
        if (access.Can(PermissionCatalog.ClaimsView, ModuleKeys.LtlGround)) hits.AddRange(await ClaimsAsync(query, ct));
        if (access.Can(PermissionCatalog.ClientsRead, ModuleKeys.Catalog)) hits.AddRange(await ClientsAsync(query, ct));
        if (access.Can(PermissionCatalog.InventoryView, ModuleKeys.WmsLotSerial))
        {
            hits.AddRange(await WarehousesAsync(query, ct));
            hits.AddRange(await ProductsAsync(query, ct));
            hits.AddRange(await DocumentsAsync(query, ct));
        }
        if (access.Can(PermissionCatalog.PurchasingView, ModuleKeys.Purchasing)) hits.AddRange(await PurchasingAsync(query, ct));
        if (access.Can(PermissionCatalog.SalesView, ModuleKeys.SalesOrders)) hits.AddRange(await SalesOrdersAsync(query, ct));
        if (access.Can(PermissionCatalog.BillingView, ModuleKeys.Billing)) hits.AddRange(await InvoicesAsync(query, ct));
        if (access.Can(PermissionCatalog.RentalView, ModuleKeys.RentalEquipment)) hits.AddRange(await RentalsAsync(query, ct));
        hits.AddRange(await PhonesAsync(query, access, ct));

        // lo exacto primero; dentro, el orden de los tipos (estable)
        var ordered = hits.Select((h, i) => (h, i)).OrderByDescending(x => x.h.Exact).ThenBy(x => x.i).Select(x => x.h)
            .GroupBy(h => (h.Type, h.Id)).Select(g => g.First()).Take(Total).ToList();
        return new SearchResultDto(query, ordered);
    }

    private async Task<Access> AccessAsync(CancellationToken ct)
    {
        IReadOnlySet<string> none = new HashSet<string>();
        var perms = !tenant.IsPlatformAdmin && tenant.UserId is int u && tenant.TenantId is int t ? await permissions.GetEffectivePermissionsAsync(u, t, ct) : none;
        var mods = tenant.TenantId is int tid ? await modules.GetEnabledKeysAsync(tid, ct) : none;
        return new Access(perms, mods, tenant.IsPlatformAdmin);
    }

    private static bool Eq(string? a, string b) => string.Equals(a?.Trim(), b, StringComparison.OrdinalIgnoreCase);
    private static SearchHitDto Hit(string type, object id, string title, string? subtitle, string matchedBy, bool exact, params (string Key, string Value)[] keys)
        => new(type, id.ToString() ?? string.Empty, title, subtitle, matchedBy, exact, keys.Length == 0 ? null : keys.ToDictionary(k => k.Key, k => k.Value));
    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    // ---------------------------------------------------------------- envíos
    private async Task<List<SearchHitDto>> OrdersAsync(string q, CancellationToken ct)
    {
        // por qué campo salió cada envío (el primero que calza manda)
        var byId = new Dictionary<int, string>();
        void Add(IEnumerable<int> ids, string field) { foreach (var id in ids) byId.TryAdd(id, field); }
        Add(await db.TransportOrders.AsNoTracking().Where(o => o.IsActive && o.WaybillNumber != null && o.WaybillNumber.StartsWith(q)).Select(o => o.TransportOrderId).Take(PerType).ToListAsync(ct), "WAYBILL");
        Add(await db.TransportOrders.AsNoTracking().Where(o => o.IsActive && o.OrderNumber.StartsWith(q)).Select(o => o.TransportOrderId).Take(PerType).ToListAsync(ct), "ORDER_NUMBER");
        Add(await db.TransportOrders.AsNoTracking().Where(o => o.IsActive && o.PackBatchNumber != null && o.PackBatchNumber.StartsWith(q)).Select(o => o.TransportOrderId).Take(PerType).ToListAsync(ct), "PACK_BATCH");
        Add(await db.TransportOrders.AsNoTracking().Where(o => o.IsActive && o.ClientInvoiceNumber != null && o.ClientInvoiceNumber.StartsWith(q)).Select(o => o.TransportOrderId).Take(PerType).ToListAsync(ct), "INVOICE");
        Add(await db.OrderPieces.AsNoTracking().Where(p => p.IsActive && p.PackageNumber.StartsWith(q)).Select(p => p.TransportOrderId).Take(PerType).ToListAsync(ct), "PACKAGE");
        Add(await db.CargoLines.AsNoTracking().Where(c => c.PackageNumber != null && c.PackageNumber.StartsWith(q)).Select(c => c.TransportOrderId).Take(PerType).ToListAsync(ct), "PACKAGE");
        Add(await db.OrderReferences.AsNoTracking().Where(r => r.RefValue.StartsWith(q)).Select(r => r.TransportOrderId).Take(PerType).ToListAsync(ct), "REFERENCE");
        if (byId.Count == 0) return new();
        var ids = byId.Keys.Take(PerType + 2).ToList();
        var rows = await db.TransportOrders.AsNoTracking().Where(o => ids.Contains(o.TransportOrderId) && o.IsActive)
            .Select(o => new { o.TransportOrderId, o.PublicId, o.OrderNumber, o.WaybillNumber, o.PackBatchNumber, o.ClientInvoiceNumber, o.ClientId }).ToListAsync(ct);
        var clientIds = rows.Select(r => r.ClientId).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).Select(c => new { c.ClientId, c.Name }).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
        return rows.Select(r =>
        {
            var field = byId[r.TransportOrderId];
            var exact = field switch { "WAYBILL" => Eq(r.WaybillNumber, q), "ORDER_NUMBER" => Eq(r.OrderNumber, q), "PACK_BATCH" => Eq(r.PackBatchNumber, q), "INVOICE" => Eq(r.ClientInvoiceNumber, q), _ => false };
            // sin texto fijo en español: la web arma «Conduce …· Empaque …» en el idioma del usuario con estas claves
            var keys = new List<(string, string)>();
            if (r.WaybillNumber is not null) keys.Add(("waybill", r.WaybillNumber));
            if (r.PackBatchNumber is not null) keys.Add(("pack", r.PackBatchNumber));
            if (clients.GetValueOrDefault(r.ClientId) is { } clientName) keys.Add(("client", clientName));
            return Hit(TypeOrder, r.PublicId, r.OrderNumber, null, field, exact, keys.ToArray());
        }).ToList();
    }

    private async Task<List<SearchHitDto>> TripsAsync(string q, CancellationToken ct)
        => (await db.Trips.AsNoTracking().Where(t => t.IsActive && t.Code.StartsWith(q)).OrderBy(t => t.Code).Take(PerType).Select(t => new { t.PublicId, t.Code, t.PlanDate }).ToListAsync(ct))
            .Select(t => Hit(TypeTrip, t.PublicId, t.Code, t.PlanDate.ToString("yyyy-MM-dd"), "TRIP_CODE", Eq(t.Code, q))).ToList();

    private async Task<List<SearchHitDto>> ClaimsAsync(string q, CancellationToken ct)
        => (await db.Claims.AsNoTracking().Where(c => c.IsActive && c.Number.StartsWith(q)).OrderBy(c => c.Number).Take(PerType).Select(c => new { c.PublicId, c.Number }).ToListAsync(ct))
            .Select(c => Hit(TypeClaim, c.PublicId, c.Number, null, "CLAIM_NUMBER", Eq(c.Number, q))).ToList();

    // ---------------------------------------------------------------- clientes
    private async Task<List<SearchHitDto>> ClientsAsync(string q, CancellationToken ct)
        => (await db.Clients.AsNoTracking().Where(c => c.IsActive && (c.Code.StartsWith(q) || c.Name.Contains(q))).OrderBy(c => c.Name).Take(PerType).Select(c => new { c.PublicId, c.Code, c.Name }).ToListAsync(ct))
            .Select(c => Hit(TypeClient, c.PublicId, c.Name, c.Code, Eq(c.Code, q) || c.Code.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? "CLIENT_CODE" : "CLIENT_NAME", Eq(c.Code, q) || Eq(c.Name, q))).ToList();

    // ---------------------------------------------------------------- almacén
    private async Task<List<SearchHitDto>> WarehousesAsync(string q, CancellationToken ct)
    {
        var hits = new List<SearchHitDto>();
        var whs = await db.Warehouses.AsNoTracking().Where(w => w.IsActive && (w.Code.StartsWith(q) || w.Name.Contains(q))).OrderBy(w => w.Code).Take(PerType).Select(w => new { w.PublicId, w.Code, w.Name }).ToListAsync(ct);
        hits.AddRange(whs.Select(w => Hit(TypeWarehouse, w.PublicId, w.Name, w.Code, w.Code.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? "WAREHOUSE_CODE" : "WAREHOUSE_NAME", Eq(w.Code, q) || Eq(w.Name, q))));
        var zones = await (from z in db.WarehouseZones.AsNoTracking() join w in db.Warehouses.AsNoTracking() on z.WarehouseId equals w.WarehouseId
                           where w.IsActive && z.Code.StartsWith(q) orderby z.Code select new { z.WarehouseZoneId, z.Code, z.Name, w.PublicId, WarehouseCode = w.Code }).Take(PerType).ToListAsync(ct);
        hits.AddRange(zones.Select(z => Hit(TypeZone, z.WarehouseZoneId, z.Code, Join(z.Name, z.WarehouseCode), "ZONE_CODE", Eq(z.Code, q), ("warehouse", z.PublicId.ToString()), ("zone", z.WarehouseZoneId.ToString()))));
        var bins = await (from b in db.WarehouseBins.AsNoTracking() join w in db.Warehouses.AsNoTracking() on b.WarehouseId equals w.WarehouseId
                          where !b.IsRemoved && w.IsActive && b.Code.StartsWith(q) orderby b.Code select new { b.WarehouseBinId, b.Code, b.WarehouseZoneId, w.PublicId, WarehouseCode = w.Code }).Take(PerType).ToListAsync(ct);
        hits.AddRange(bins.Select(b => Hit(TypeBin, b.WarehouseBinId, b.Code, b.WarehouseCode, "BIN_CODE", Eq(b.Code, q), ("warehouse", b.PublicId.ToString()), ("zone", b.WarehouseZoneId.ToString()), ("bin", b.Code))));
        return hits;
    }

    private async Task<List<SearchHitDto>> ProductsAsync(string q, CancellationToken ct)
    {
        var contains = q.Length >= 3;   // con 2 letras solo prefijo: evita traer medio catálogo
        var rows = await db.Products.AsNoTracking().Where(p => p.IsActive && (p.Sku.StartsWith(q) || (p.Barcode != null && p.Barcode == q) || (contains ? p.Name.Contains(q) : p.Name.StartsWith(q))))
            .OrderBy(p => p.Sku).Take(PerType).Select(p => new { p.PublicId, p.Sku, p.Name, p.Barcode }).ToListAsync(ct);
        return rows.Select(p => Hit(TypeProduct, p.PublicId, p.Sku, p.Name,
            p.Sku.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? "SKU" : Eq(p.Barcode, q) ? "BARCODE" : "PRODUCT_NAME", Eq(p.Sku, q) || Eq(p.Barcode, q) || Eq(p.Name, q))).ToList();
    }

    /// <summary>Recibos, recolecciones/empaques/despachos manuales y avisos de salida: su número por prefijo (REC-, EMP-, DMA-, SAL-).</summary>
    private async Task<List<SearchHitDto>> DocumentsAsync(string q, CancellationToken ct)
    {
        var hits = new List<SearchHitDto>();
        hits.AddRange((await db.ReceiptHeaders.AsNoTracking().Where(r => r.IsActive && r.Number.StartsWith(q)).OrderBy(r => r.Number).Take(PerType).Select(r => new { r.PublicId, r.Number, r.Reference }).ToListAsync(ct))
            .Select(r => Hit(TypeReceipt, r.PublicId, r.Number, r.Reference, "RECEIPT_NUMBER", Eq(r.Number, q))));
        hits.AddRange((await db.PickBatches.AsNoTracking().Where(p => p.IsActive && p.Number.StartsWith(q)).OrderBy(p => p.Number).Take(PerType).Select(p => new { p.PublicId, p.Number }).ToListAsync(ct))
            .Select(p => Hit(TypePickBatch, p.PublicId, p.Number, null, "PICK_BATCH_NUMBER", Eq(p.Number, q))));
        hits.AddRange((await db.Set<ExitNotice>().AsNoTracking().Where(n => n.Number.StartsWith(q)).OrderBy(n => n.Number).Take(PerType).Select(n => new { n.PublicId, n.Number, n.Reference }).ToListAsync(ct))
            .Select(n => Hit(TypeExitNotice, n.PublicId, n.Number, n.Reference, "EXIT_NOTICE_NUMBER", Eq(n.Number, q))));
        return hits;
    }

    // ---------------------------------------------------------------- compras, ventas, facturas, rentas
    private async Task<List<SearchHitDto>> PurchasingAsync(string q, CancellationToken ct)
    {
        var hits = new List<SearchHitDto>();
        hits.AddRange((await db.PurchaseOrders.AsNoTracking().Where(p => p.Number.StartsWith(q)).OrderBy(p => p.Number).Take(PerType).Select(p => new { p.PublicId, p.Number }).ToListAsync(ct))
            .Select(p => Hit(TypePurchaseOrder, p.PublicId, p.Number, null, "PURCHASE_ORDER_NUMBER", Eq(p.Number, q))));
        hits.AddRange((await db.Suppliers.AsNoTracking().Where(s => s.IsActive && s.Name.Contains(q)).OrderBy(s => s.Name).Take(PerType).Select(s => new { s.SupplierId, s.Name }).ToListAsync(ct))
            .Select(s => Hit(TypeSupplier, s.SupplierId, s.Name, null, "SUPPLIER_NAME", Eq(s.Name, q))));
        return hits;
    }

    private async Task<List<SearchHitDto>> SalesOrdersAsync(string q, CancellationToken ct)
        => (await db.Set<SalesOrder>().AsNoTracking().Where(s => s.Number.StartsWith(q)).OrderBy(s => s.Number).Take(PerType).Select(s => new { s.PublicId, s.Number }).ToListAsync(ct))
            .Select(s => Hit(TypeSalesOrder, s.PublicId, s.Number, null, "SALES_ORDER_NUMBER", Eq(s.Number, q))).ToList();

    private async Task<List<SearchHitDto>> InvoicesAsync(string q, CancellationToken ct)
        => (await db.Set<Invoice>().AsNoTracking().Where(i => i.IsActive && i.InvoiceNumber.StartsWith(q)).OrderBy(i => i.InvoiceNumber).Take(PerType).Select(i => new { i.PublicId, i.InvoiceNumber }).ToListAsync(ct))
            .Select(i => Hit(TypeInvoice, i.PublicId, i.InvoiceNumber, null, "INVOICE_NUMBER", Eq(i.InvoiceNumber, q))).ToList();

    private async Task<List<SearchHitDto>> RentalsAsync(string q, CancellationToken ct)
        => (await db.Rentals.AsNoTracking().Where(r => r.Number.StartsWith(q)).OrderBy(r => r.Number).Take(PerType).Select(r => new { r.PublicId, r.Number }).ToListAsync(ct))
            .Select(r => Hit(TypeRental, r.PublicId, r.Number, null, "RENTAL_NUMBER", Eq(r.Number, q))).ToList();

    // ---------------------------------------------------------------- teléfonos
    /// <summary>
    /// Teléfono: se compara por dígitos (los números se guardan sin formato): igual o por prefijo del número nacional (con «+prefijo» se quita el prefijo;
    /// con 11 dígitos que empiezan con 1, también). Lleva al dueño del contacto (cliente, persona del cliente, consignatario o envío) si el usuario puede verlo.
    /// </summary>
    private async Task<List<SearchHitDto>> PhonesAsync(string q, Access access, CancellationToken ct)
    {
        if (q.Any(c => !(char.IsDigit(c) || c is '+' or ' ' or '-' or '(' or ')' or '.'))) return new();
        var digits = PhoneRules.Digits(q);
        if (digits.Length < 7) return new();
        var national = digits.Length == 11 && digits[0] == '1' ? digits[1..] : digits;
        if (q.TrimStart().StartsWith('+')) { try { national = PhoneRules.Parse(q, null, "1").National; } catch (FormatException) { /* se usan los dígitos tal cual */ } }
        var rows = await (from c in db.ContactPoints.AsNoTracking()
                          join o in db.LookupCodes.AsNoTracking() on c.OwnerEntityLookupId equals o.LookupCodeId
                          join t in db.LookupCodes.AsNoTracking() on c.ContactTypeLookupId equals t.LookupCodeId
                          where c.IsActive && c.Value.StartsWith(national) && (t.InternalCode == "PHONE" || t.InternalCode == "MOBILE" || t.InternalCode == "WHATSAPP" || t.InternalCode == "FAX")
                          select new { c.Value, c.OwnerId, Owner = o.InternalCode }).Take(PerType * 2).ToListAsync(ct);
        var hits = new List<SearchHitDto>();
        foreach (var r in rows)
        {
            if (!PermissionCatalog.OwnerReadPermission.TryGetValue(r.Owner, out var perm) || !access.HasPermission(perm)) continue;
            var exact = r.Value == national;
            switch (r.Owner.ToUpperInvariant())
            {
                case "CLIENT":
                    foreach (var c in await db.Clients.AsNoTracking().Where(x => x.ClientId == r.OwnerId).Select(x => new { x.PublicId, x.Name, x.Code }).ToListAsync(ct))
                        hits.Add(Hit(TypeClient, c.PublicId, c.Name, Join(c.Code, FormatPhone(r.Value)), "PHONE", exact));
                    break;
                case "CLIENT_CONTACT":
                    foreach (var c in await (from p in db.ClientContacts.AsNoTracking() join cl in db.Clients.AsNoTracking() on p.ClientId equals cl.ClientId where p.ClientContactId == r.OwnerId select new { cl.PublicId, cl.Name, p.FullName }).ToListAsync(ct))
                        hits.Add(Hit(TypeClient, c.PublicId, c.Name, Join(c.FullName, FormatPhone(r.Value)), "PHONE", exact));
                    break;
                case "LOCATION":
                    foreach (var l in await db.Locations.AsNoTracking().Where(x => x.LocationId == r.OwnerId && x.IsActive).Select(x => new { x.PublicId, x.Name, x.City }).ToListAsync(ct))
                        hits.Add(Hit(TypeConsignee, l.PublicId, l.Name, Join(l.City, FormatPhone(r.Value)), "PHONE", exact));
                    break;
                case "TRANSPORT_ORDER":
                    foreach (var o in await db.TransportOrders.AsNoTracking().Where(x => x.TransportOrderId == r.OwnerId && x.IsActive).Select(x => new { x.PublicId, x.OrderNumber }).ToListAsync(ct))
                        hits.Add(Hit(TypeOrder, o.PublicId, o.OrderNumber, FormatPhone(r.Value), "PHONE", exact));
                    break;
            }
        }
        // proveedores: su teléfono es una columna del propio proveedor
        if (access.HasPermission(PermissionCatalog.PurchasingView))
            hits.AddRange((await db.Suppliers.AsNoTracking().Where(s => s.IsActive && s.Phone != null && s.Phone.StartsWith(national)).Take(PerType).Select(s => new { s.SupplierId, s.Name, s.Phone }).ToListAsync(ct))
                .Select(s => Hit(TypeSupplier, s.SupplierId, s.Name, FormatPhone(s.Phone ?? string.Empty), "PHONE", s.Phone == national)));
        return hits.Take(PerType).ToList();
    }

    private static string FormatPhone(string national) => national.Length == 10 ? $"({national[..3]}) {national[3..6]}-{national[6..]}" : national;
}
