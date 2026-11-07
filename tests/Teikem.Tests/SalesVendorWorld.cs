using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Files;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;

namespace Teikem.Tests;

/// <summary>
/// Lote 222 (P2 del pedido de vendedores) — el mundo de las pruebas de servicio: WmsFixture con órdenes de venta, clientes (alta mínima, aprobar, rechazar), documentos (almacén
/// de archivos cifrado en una carpeta temporal), avisos, «Necesita tu atención» y el panel Ventas del Pulso. Suma los catálogos del lote (formas de pago, términos, motivos de envío,
/// tipos de documento, tipo de cargo SHIPPING) y los estatus PROSPECT (antes de ACTIVE) y SUSPENDED (lateral) del cliente, como logistica-db-update.sql.
/// </summary>
internal sealed class SalesVendorWorld : IAsyncDisposable
{
    public static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    private static readonly string Key = Convert.ToBase64String(Encoding.UTF8.GetBytes("llave-de-prueba-de-32-bytes-0001"));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "teikem-l222-" + Guid.NewGuid().ToString("N"));

    public WmsFixture F { get; private set; } = null!;
    public Warehouse W { get; private set; } = null!;
    public WarehouseBin Bin { get; private set; } = null!;
    public SalesOrderService Sales => F.Get<SalesOrderService>();
    public ClientProspectService Prospects => F.Get<ClientProspectService>();
    public ClientService Clients => F.Get<ClientService>();
    public ClientDocumentService Documents => F.Get<ClientDocumentService>();

    public static async Task<SalesVendorWorld> CreateAsync()
    {
        var x = new SalesVendorWorld();
        var opts = Options.Create(new FileStoreOptions { Root = x._root, MasterKey = Key, MaxMegabytes = 10 });
        x.F = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<INotificationEventProvider, SalesEventProvider>();
            s.AddSingleton<NotificationEventCatalog>();
            s.AddSingleton<INotificationPublisher, NotificationPublisher>();
            s.AddSingleton<SalesOrderService>();
            s.AddSingleton<IOwnedEntityResolver, ClientOwnedEntityResolver>();
            s.AddSingleton<IOwnedEntityResolver, ClientContactOwnedEntityResolver>();
            s.AddSingleton<ContactPointService>();
            s.AddSingleton<LocationService>();
            s.AddSingleton<ClientService>();
            s.AddSingleton<ClientProspectService>();
            s.AddSingleton<IOptions<FileStoreOptions>>(opts);
            s.AddSingleton<IFileStore>(new EncryptedDiskFileStore(opts));
            s.AddSingleton<StoredFileService>();
            s.AddSingleton<ClientDocumentService>();
            s.AddSingleton<SalesPulseService>();
            s.AddSingleton<SalesClientPendingAttentionProvider>();
            s.AddSingleton<SalesUrgentAttentionProvider>();
            s.AddSingleton<RateService>();
            s.AddSingleton<OrderQuoteService>();
            s.AddSingleton<BillingService>();
        });
        x.F.SetModules(ModuleKeys.SalesOrders, ModuleKeys.Catalog, ModuleKeys.Billing);
        await x.SeedLote222Async();
        x.W = await x.F.AddWarehouseAsync("W1");
        x.Bin = await x.F.AddBinAsync(await x.F.AddZoneAsync(x.W, "PZ", ZoneTypes.Picking), "P-01");
        return x;
    }

    /// <summary>Catálogos y estatus del lote (al final: los ids del fixture no cambian).</summary>
    private async Task SeedLote222Async()
    {
        var db = F.Db;
        var id = 5000;
        var added = new List<LookupCode>();
        void L(string entity, string code) => added.Add(new LookupCode { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true });
        foreach (var c in SalesPaymentMethods.All) L(SalesPaymentMethods.Domain, c);
        foreach (var c in new[] { "COD", "NET15", "NET20", "NET30", "NET45", "NET60", "CHEQUE", "CASH", "ACH", "CONSIGNMENT", "PK_BY_REP" }) L(LookupDomains.PaymentTerm, c);
        foreach (var c in ShippingReasons.All) L(ShippingReasons.Domain, c);
        foreach (var c in ClientDocumentCodes.Types) L(ClientDocumentCodes.TypeDomain, c);
        L(Teikem.Domain.Billing.BillingCodes.ChargeTypeDomain, SalesShippingCodes.ChargeType);
        L(LookupDomains.EntityType, EntityTypes.ClientContact);
        L(LookupDomains.EntityType, ClientDocumentCodes.EntityType);
        foreach (var c in new[] { "PHONE", "EMAIL", "MOBILE" }) L(LookupDomains.ContactType, c);
        L(LookupDomains.LocationType, LocationTypes.Corporate);
        L(LookupDomains.LocationType, LocationTypes.Billing);
        L(LookupDomains.LocationType, LocationTypes.Pickup);
        L(LookupDomains.LocationType, LocationTypes.Both);
        db.LookupCodes.AddRange(added);
        var pipe = await db.LookupCodes.AsNoTracking().SingleAsync(l => l.Entity == LookupDomains.StageKind && l.InternalCode == StageKinds.Pipeline);
        var lat = await db.LookupCodes.AsNoTracking().SingleAsync(l => l.Entity == LookupDomains.StageKind && l.InternalCode == StageKinds.Lateral);
        db.StatusCodes.Add(new StatusCode { StatusCodeId = 5000, Entity = StatusDomains.ClientStatus, InternalCode = ClientStatuses.Prospect, LabelJson = "{\"es\":\"Prospecto\"}", StageKindLookupId = pipe.LookupCodeId, SortOrder = 0, IsActive = true });
        db.StatusCodes.Add(new StatusCode { StatusCodeId = 5002, Entity = StatusDomains.ContractStatus, InternalCode = ContractStatuses.Draft, LabelJson = "{\"es\":\"Borrador\"}", StageKindLookupId = pipe.LookupCodeId, SortOrder = 1, IsInitial = true, IsActive = true });
        db.StatusCodes.Add(new StatusCode { StatusCodeId = 5001, Entity = StatusDomains.ClientStatus, InternalCode = ClientStatuses.Suspended, LabelJson = "{\"es\":\"Suspendido\"}", StageKindLookupId = lat.LookupCodeId, SortOrder = 3, IsActive = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        F.Lookups.Load(await db.LookupCodes.AsNoTracking().ToListAsync());
    }

    public int ProspectStatusId => 5000;
    public int SuspendedStatusId => 5001;

    public async Task<Product> ProductAsync(string sku, decimal price, decimal stock = 100m)
    {
        var p = await F.AddProductAsync(sku);
        var row = await F.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == p.ProductId);
        row.SalePrice = price;
        if (stock > 0m)
            F.Db.StockBalances.Add(new StockBalance { TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = W.WarehouseId, WarehouseBinId = Bin.WarehouseBinId, QtyOnHand = stock, UpdatedAtUtc = DateTime.UtcNow });
        await F.Db.SaveChangesAsync();
        F.Db.ChangeTracker.Clear();
        return p;
    }

    /// <summary>Un cliente existente con lo que la prueba le quiera poner (término de pago, regla de envío, firma…).</summary>
    public async Task<Client> ClientAsync(string code, Action<Client>? set = null)
    {
        var c = await F.AddClientAsync(code);
        if (set is not null)
        {
            var row = await F.Db.Clients.AsTracking().SingleAsync(r => r.ClientId == c.ClientId);
            set(row);
            await F.Db.SaveChangesAsync();
            F.Db.ChangeTracker.Clear();
        }
        return await F.Db.Clients.AsNoTracking().SingleAsync(r => r.ClientId == c.ClientId);
    }

    public int Lookup(string domain, string code) => F.Db.LookupCodes.AsNoTracking().Single(l => l.Entity == domain && l.InternalCode == code).LookupCodeId;

    public async Task SetCompanyShippingAsync(decimal? charge, decimal? freeFrom, bool taxable = false)
    {
        var t = await F.Db.Tenants.AsTracking().SingleAsync(x => x.TenantId == WmsFixture.TenantId);
        (t.SalesShippingCharge, t.SalesShippingFreeFrom, t.SalesShippingTaxable) = (charge, freeFrom, taxable);
        await F.Db.SaveChangesAsync();
        F.Db.ChangeTracker.Clear();
    }

    /// <summary>Un consignatario del cliente (para que la orden sea de entrega).</summary>
    public async Task<Location> ConsigneeAsync(Client c)
    {
        var l = new Location
        {
            TenantId = WmsFixture.TenantId, ClientId = c.ClientId, PublicId = Guid.NewGuid(), Name = "Farmacia " + c.Code, Line1 = "Calle 1", City = "Ponce", IsActive = true,
            LocationTypeLookupId = Lookup(LookupDomains.LocationType, LocationTypes.Delivery), CountryLookupId = Lookup(LookupDomains.Country, "PR"), CreatedAtUtc = DateTime.UtcNow,
        };
        F.Db.Locations.Add(l);
        await F.Db.SaveChangesAsync();
        F.Db.ChangeTracker.Clear();
        return l;
    }

    public Task<SalesOrderDto> DraftAsync(Client c, Product p, decimal qty, SalesOrderRequest? extra = null, Guid? consignee = null)
        => Sales.CreateAsync((extra ?? new SalesOrderRequest()) with
        {
            WarehousePublicId = W.PublicId, ClientPublicId = c.PublicId, ConsigneePublicId = consignee ?? extra?.ConsigneePublicId,
            Lines = new[] { new SalesOrderLineRequest(p.PublicId, qty) },
        }, default);

    public static ClientBasicCreateRequest BasicRequest(string name = "Farmacia Nueva") => new(
        Name: name, LegalName: "Farmacia Nueva LLC", ContactName: "Ana Rivera", Phone: "7875551234", PhoneExtension: "12", Phone2: "7875559876", Email: "ana@farmacia.com",
        MerchantRegistration: "SURI-12345", PhysicalAddress: new ClientBasicAddressRequest("Calle Sol 10", null, "Ponce", "00716"),
        PostalAddress: new ClientBasicAddressRequest("PO Box 55", null, "Ponce", "00732"), PaymentTerm: "NET30");

    public async ValueTask DisposeAsync()
    {
        await F.DisposeAsync();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* temporal */ }
    }
}
