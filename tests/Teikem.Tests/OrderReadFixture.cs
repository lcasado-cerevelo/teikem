using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Tests;

/// <summary>
/// Lote 39 — fixture InMemory de la LECTURA de órdenes: OrderReadService, StatusService y PermissionService reales, con la
/// decisión de precios (OrderPriceVisibility) real. El usuario arranca como administrador de plataforma (todo permiso);
/// <see cref="SetPermissions"/> lo deja solo con los códigos dados. Cuatro órdenes del tenant 1 y una del tenant 2:
/// <list type="bullet">
/// <item>O1: Cliente Uno · STANDARD · DRAFT · Bayamón 00961 · BOX ×2 · sin cotizar</item>
/// <item>O2: Cliente Uno · EXPRESS · CONFIRMED · San Juan 00901 · ENVELOPE ×1 (+ PALLET inactiva) · cotizada 26.25 · COD 200</item>
/// <item>O3: Cliente Dos · STANDARD · CONFIRMED · bayamon 00956 · PALLET ×3 · cotizada 40</item>
/// <item>O4: Cliente Tres · EXPRESS · CANCELLED · Caguas 00725 · BOX ×1 · cotizada 10</item>
/// <item>X (tenant 2): STANDARD · CONFIRMED · Bayamón 00961 · BOX — nunca aparece</item>
/// </list>
/// </summary>
internal sealed class OrderReadFixture : IAsyncDisposable
{
    public const int TenantId = 1;
    private readonly Dictionary<string, int> _status = new(StringComparer.OrdinalIgnoreCase);

    private OrderReadFixture(TenantContext tenant, TeikemDbContext db, TripTestLookups lookups, MemoryCache cache)
    {
        Tenant = tenant;
        Db = db;
        Lookups = lookups;
        Cache = cache;
        Permissions = new PermissionService(db, tenant, cache, lookups, new ReceivingNullSecurityEventWriter());
        Prices = new OrderPriceVisibility(Permissions);
        Statuses = new StatusService(db, tenant, lookups, Array.Empty<IStatusTransitionEffect>(), Permissions);
        Reader = new OrderReadService(db, tenant, lookups, Statuses, Prices);
    }

    public TenantContext Tenant { get; }
    public TeikemDbContext Db { get; }
    public TripTestLookups Lookups { get; }
    public MemoryCache Cache { get; }
    public PermissionService Permissions { get; }
    public OrderPriceVisibility Prices { get; }
    public StatusService Statuses { get; }
    public OrderReadService Reader { get; }

    public Client C1 { get; private set; } = null!;
    public Client C2 { get; private set; } = null!;
    public Client C3 { get; private set; } = null!;
    public TransportOrder O1 { get; private set; } = null!;
    public TransportOrder O2 { get; private set; } = null!;
    public TransportOrder O3 { get; private set; } = null!;
    public TransportOrder O4 { get; private set; } = null!;

    public int StatusId(string domain, string code) => _status[domain + "|" + code];

    /// <summary>El usuario deja de ser administrador de plataforma y queda SOLO con estos permisos.</summary>
    public void SetPermissions(params string[] codes)
    {
        Tenant.IsPlatformAdmin = false;
        Cache.Set($"perms:{Tenant.UserId}:{Tenant.TenantId}", new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase));
    }

    public static async Task<OrderReadFixture> CreateAsync()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1, IsAuthenticated = true, IsPlatformAdmin = true };
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("order-read-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var f = new OrderReadFixture(tenant, new TeikemDbContext(options, tenant), new TripTestLookups(), new MemoryCache(new MemoryCacheOptions()));
        await f.SeedAsync();
        return f;
    }

    private async Task SeedAsync()
    {
        var id = 1;
        LookupCode L(string entity, string code) => new() { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
        var pipe = L(LookupDomains.StageKind, StageKinds.Pipeline);
        var lat = L(LookupDomains.StageKind, StageKinds.Lateral);
        var term = L(LookupDomains.StageKind, StageKinds.Terminal);
        var delivery = L(LookupDomains.StopType, StopTypes.Delivery);
        var standard = L(LookupDomains.ServiceType, "STANDARD");
        var express = L(LookupDomains.ServiceType, "EXPRESS");
        var box = L(LookupDomains.PackageType, "BOX");
        var envelope = L(LookupDomains.PackageType, "ENVELOPE");
        var pallet = L(LookupDomains.PackageType, "PALLET");
        var all = new List<LookupCode>
        {
            pipe, lat, term, delivery, standard, express, box, envelope, pallet,
            L(LookupDomains.StopType, StopTypes.Pickup),
            L(LookupDomains.ServiceType, "NEXT_DAY"),
            L(LookupDomains.EntityType, EntityTypes.TransportOrder),
            L(LookupDomains.EntityType, EntityTypes.Client),
            L(LookupDomains.Capability, Capabilities.EditCargo),
            L(LookupDomains.Capability, Capabilities.Reprice),
            L(LookupDomains.Capability, Capabilities.Cancel),
        };
        Db.LookupCodes.AddRange(all);
        Lookups.Load(all);

        var sid = 100;
        void S(string domain, string code, LookupCode kind, int sort, bool initial = false)
        {
            var s = new StatusCode { StatusCodeId = sid++, Entity = domain, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", StageKindLookupId = kind.LookupCodeId, SortOrder = sort, IsInitial = initial, IsActive = true };
            Db.StatusCodes.Add(s);
            _status[domain + "|" + code] = s.StatusCodeId;
        }
        S(StatusDomains.OrderStatus, OrderStatuses.Draft, pipe, 1, true);
        S(StatusDomains.OrderStatus, OrderStatuses.Confirmed, pipe, 2);
        S(StatusDomains.OrderStatus, OrderStatuses.OnHold, lat, 20);
        S(StatusDomains.OrderStatus, OrderStatuses.Cancelled, term, 30);
        S(StatusDomains.StopStatus, StopStatuses.Pending, pipe, 1, true);
        S(ImportBatchStatuses.Domain, ImportBatchStatuses.Validated, pipe, 1, true);
        S(ImportBatchStatuses.Domain, ImportBatchStatuses.Confirmed, term, 2);

        Client C(int clientId, string name, int tenantId = TenantId) => new()
        {
            ClientId = clientId, PublicId = Guid.NewGuid(), TenantId = tenantId, Code = "C" + clientId, Name = name,
            StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        C1 = C(1, "Cliente Uno");
        C2 = C(2, "Cliente Dos");
        C3 = C(3, "Cliente Tres");
        Db.Clients.AddRange(C1, C2, C3, C(9, "Cliente ajeno", tenantId: 2));

        var stopId = 900;
        var lineId = 9000;
        var minute = 0;
        TransportOrder O(int orderId, int clientId, LookupCode service, string status, string city, string zip, decimal? quoted,
            IEnumerable<(LookupCode Type, int Pieces, bool Active)> lines, int tenantId = TenantId, decimal? cod = null)
        {
            var o = new TransportOrder
            {
                TransportOrderId = orderId, PublicId = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId,
                OrderNumber = "2026-" + orderId, PackBatchNumber = "EMP-" + orderId, ClientInvoiceNumber = "F-" + orderId,
                ServiceTypeLookupId = service.LookupCodeId, StatusCodeId = StatusId(StatusDomains.OrderStatus, status),
                QuotedAmount = quoted, CodAmount = cod, IsActive = true,
                CreatedAtUtc = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc).AddMinutes(minute++),
            };
            o.Stops.Add(new OrderStop
            {
                OrderStopId = stopId++, TransportOrderId = orderId, StopTypeLookupId = delivery.LookupCodeId, Sequence = 2,
                SnapName = "Consignatario " + orderId, SnapLine1 = "Calle 1", SnapCity = city, SnapPostalCode = zip,
                StatusCodeId = StatusId(StatusDomains.StopStatus, StopStatuses.Pending),
            });
            foreach (var (type, pieces, active) in lines)
                o.CargoLines.Add(new CargoLine
                {
                    CargoLineId = lineId++, TransportOrderId = orderId, PackageTypeLookupId = type.LookupCodeId,
                    Description = type.InternalCode, Quantity = pieces, IsActive = active,
                });
            Db.TransportOrders.Add(o);
            return o;
        }

        O1 = O(1, 1, standard, OrderStatuses.Draft, "Bayamón", "00961", null, new[] { (box, 2, true) });
        O2 = O(2, 1, express, OrderStatuses.Confirmed, "San Juan", "00901", 26.25m, new[] { (envelope, 1, true), (pallet, 4, false) }, cod: 200m);
        O3 = O(3, 2, standard, OrderStatuses.Confirmed, "bayamon", "00956", 40m, new[] { (pallet, 3, true) });
        O4 = O(4, 3, express, OrderStatuses.Cancelled, "Caguas", "00725", 10m, new[] { (box, 1, true) });
        O(5, 9, standard, OrderStatuses.Confirmed, "Bayamón", "00961", 99m, new[] { (box, 1, true) }, tenantId: 2);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        Cache.Dispose();
    }
}
