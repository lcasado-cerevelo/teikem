using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 232 — kit armado contra SQL Server REAL (base desechable de NotificationSqlFixture; se omite sin TEIKEM_TEST_SQLSERVER): las consultas se traducen (candidatos con
/// zona y lote, capas de costo unidas, historial), el contador real (NumberSequenceService), el bloqueo real del ledger (dos armados a la vez que comparten pieza no dejan
/// existencia negativa: uno pasa y el otro es 409) y el valor total del inventario igual al armar con costo promedio y con capas (FIFO).
/// </summary>
public sealed class AssembledKitSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    private static readonly SemaphoreSlim SeedGate = new(1, 1);

    /// <summary>Catálogos globales que necesitan el ledger y el servicio (la base de pruebas no tiene el seed). Idempotente.</summary>
    private async Task SeedLookupsAsync()
    {
        await SeedGate.WaitAsync();
        try
        {
            await using var db = fx.NewDb(new TenantContext());
            var wanted = new (string Entity, string Code)[]
            {
                (LookupDomains.UnitOfMeasure, "UN"), (LookupDomains.TrackingType, TrackingTypes.None), (LookupDomains.TrackingType, TrackingTypes.Lot),
                (LookupDomains.TrackingType, TrackingTypes.Serial), (LookupDomains.ZoneType, ZoneTypes.Picking), (LookupDomains.ZoneType, ZoneTypes.Quarantine),
                (LookupDomains.InventoryTxnType, InventoryTxnTypes.Adjustment), (LookupDomains.AdjustmentReason, AdjustmentReasons.Found),
                (LookupDomains.AdjustmentReason, AdjustmentReasons.KitAssembly), (LookupDomains.AdjustmentReason, AdjustmentReasons.KitDisassembly),
                (LookupDomains.EntityType, AssembledKitCodes.AssemblyEntityType), (LookupDomains.EntityType, AssembledKitCodes.DisassemblyEntityType),
                (KitCodes.ProductTypeDomain, ProductTypes.Kit), (KitCodes.ProductTypeDomain, ProductTypes.AssembledKit),
            };
            foreach (var (entity, code) in wanted)
                if (!await db.Set<LookupCode>().AnyAsync(l => l.Entity == entity && l.InternalCode == code))
                    db.Set<LookupCode>().Add(new LookupCode { Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true, IsSystem = true });
            await db.SaveChangesAsync();
        }
        finally { SeedGate.Release(); }
    }

    private sealed class SqlLookups(NotificationSqlFixture fx) : ILookupCache
    {
        public async Task<int> GetIdAsync(string entity, string code, CancellationToken ct = default) => (await TryGetIdAsync(entity, code, ct)) ?? throw new InvalidOperationException($"{entity}/{code}");
        public async Task<int?> TryGetIdAsync(string entity, string code, CancellationToken ct = default)
        {
            await using var db = fx.NewDb(new TenantContext());
            return await db.Set<LookupCode>().Where(l => l.Entity == entity && l.InternalCode == code).Select(l => (int?)l.LookupCodeId).FirstOrDefaultAsync(ct);
        }
        public async Task<LookupCode?> GetAsync(int lookupCodeId, CancellationToken ct = default)
        {
            await using var db = fx.NewDb(new TenantContext());
            return await db.Set<LookupCode>().FirstOrDefaultAsync(l => l.LookupCodeId == lookupCodeId, ct);
        }
        public async Task<IReadOnlyList<LookupCode>> GetDomainAsync(string entity, CancellationToken ct = default)
        {
            await using var db = fx.NewDb(new TenantContext());
            return await db.Set<LookupCode>().Where(l => l.Entity == entity).ToListAsync(ct);
        }
        public void Invalidate() { }
    }

    /// <summary>Un contenedor por «petición»: su propio DbContext, el ledger y los servicios reales; el usuario es administrador (todo permiso).</summary>
    private ServiceProvider Session(int tenantId)
    {
        var tenant = new TenantContext { TenantId = tenantId, UserId = null, IsAuthenticated = true, IsPlatformAdmin = true };
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton(tenant);
        s.AddSingleton<ITenantContext>(tenant);
        s.AddSingleton(fx.NewDb(tenant));
        s.AddSingleton<ILookupCache>(new SqlLookups(fx));
        s.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        s.AddSingleton<ISecurityEventWriter, ReceivingNullSecurityEventWriter>();
        s.AddSingleton<PermissionService>();
        s.AddSingleton(sp => new StatusService(sp.GetRequiredService<TeikemDbContext>(), tenant, sp.GetRequiredService<ILookupCache>(), Array.Empty<IStatusTransitionEffect>(),
            sp.GetRequiredService<PermissionService>()));
        s.AddSingleton<INumberSequenceService, NumberSequenceService>();
        s.AddSingleton<InventoryChangeSink>();
        s.AddSingleton<IInventoryChangeSink>(sp => sp.GetRequiredService<InventoryChangeSink>());
        s.AddSingleton<InventoryLedger>();
        s.AddSingleton<ITenantClock>(TenantClock.Default);
        s.AddSingleton<InventoryReadService>();
        s.AddSingleton<ProductService>();
        s.AddSingleton<ValuationService>();
        s.AddSingleton<AssembledKitService>();
        return s.BuildServiceProvider();
    }

    private sealed record World(int TenantId, int WarehouseId, Guid WarehousePublicId, int Bin1, int Bin2, int KitBin, Dictionary<string, Product> Products, Dictionary<string, int> Lots);

    /// <summary>Almacén con dos posiciones de recolección y una para los kits; A (lote: A1 vence 2027-03-01 ×10, A2 vence 2027-01-15 ×4) y B (sin lote).</summary>
    private async Task<World> SeedAsync(string name, decimal stockB, string? valuationMethod = null, IReadOnlyList<(decimal Qty, decimal Cost)>? layersB = null)
    {
        await SeedLookupsAsync();
        var tenantId = await fx.NewTenantAsync(name);
        var lookups = new SqlLookups(fx);
        await using (var db = fx.NewDb(tenantId))
        {
            var t = await db.Tenants.SingleAsync(x => x.TenantId == tenantId);
            t.UsesAssembledKits = true;   // Lote 233: el alta de un kit armado pide el ajuste de la compañía (apagado por omisión)
            if (valuationMethod is not null) t.ValuationMethod = valuationMethod;
            var w = new Warehouse { PublicId = Guid.NewGuid(), TenantId = tenantId, Code = "W1", Name = "Almacén", CountryLookupId = 1, StatusCodeId = 1, IsActive = true };
            db.Warehouses.Add(w);
            await db.SaveChangesAsync();
            var zone = new WarehouseZone { WarehouseId = w.WarehouseId, Code = "PCK", Name = "Recolección", ZoneTypeLookupId = await lookups.GetIdAsync(LookupDomains.ZoneType, ZoneTypes.Picking), IsActive = true };
            db.WarehouseZones.Add(zone);
            await db.SaveChangesAsync();
            var bins = new[] { "P-01", "P-02", "K-01" }.Select(c => new WarehouseBin { WarehouseZoneId = zone.WarehouseZoneId, WarehouseId = w.WarehouseId, Code = c, IsActive = true }).ToList();
            db.WarehouseBins.AddRange(bins);
            var uom = await lookups.GetIdAsync(LookupDomains.UnitOfMeasure, "UN");
            Product P(string sku, string tracking, decimal cost) => new()
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, Sku = sku, Name = sku, BaseUomLookupId = uom, IsActive = true, PurchaseCost = cost,
                TrackingTypeLookupId = lookups.GetIdAsync(LookupDomains.TrackingType, tracking).GetAwaiter().GetResult(),
            };
            var a = P("A", TrackingTypes.Lot, 3m);
            var b = P("B", TrackingTypes.None, 4m);
            db.Products.AddRange(a, b);
            await db.SaveChangesAsync();
            var a1 = new InventoryLot { ProductId = a.ProductId, LotNumber = "A1", ExpiryDate = new DateOnly(2027, 3, 1), IsActive = true };
            var a2 = new InventoryLot { ProductId = a.ProductId, LotNumber = "A2", ExpiryDate = new DateOnly(2027, 1, 15), IsActive = true };
            db.InventoryLots.AddRange(a1, a2);
            if (layersB is not null)
            {
                // capas de costo de B: recibos confirmados (la valuación usa las líneas de recibo); el saldo se carga con el ajuste de abajo
                var at = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
                foreach (var (qty, cost) in layersB)
                {
                    var h = new ReceiptHeader { PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = w.WarehouseId, ReceiptTypeLookupId = 1, Number = "REC-" + Guid.NewGuid().ToString("N")[..6],
                        StatusCodeId = 1, ReceivedAtUtc = at, IsActive = true, CreatedAtUtc = at };
                    h.Lines.Add(new ReceiptLine { ProductId = b.ProductId, ReceivedQty = qty, UnitCost = cost });
                    db.Add(h);
                    at = at.AddDays(1);
                }
            }
            await db.SaveChangesAsync();
            using var seed = Session(tenantId);
            var sdb = seed.GetRequiredService<TeikemDbContext>();
            var ledger = seed.GetRequiredService<InventoryLedger>();
            InventoryPosting In(Product p, decimal q, int bin, int? lot = null)
                => new(InventoryTxnTypes.Adjustment, p.ProductId, q, LotId: lot, ToWarehouseId: w.WarehouseId, ToBinId: bin, ReasonCode: AdjustmentReasons.Found, Notes: "saldo");
            await sdb.RunInTransactionAsync(ct => ledger.PostAsync(new[] { In(a, 10m, bins[0].WarehouseBinId, a1.LotId), In(a, 4m, bins[1].WarehouseBinId, a2.LotId), In(b, stockB, bins[0].WarehouseBinId) }, ct), default);
            return new World(tenantId, w.WarehouseId, w.PublicId, bins[0].WarehouseBinId, bins[1].WarehouseBinId, bins[2].WarehouseBinId,
                new Dictionary<string, Product> { ["A"] = a, ["B"] = b }, new Dictionary<string, int> { ["A1"] = a1.LotId, ["A2"] = a2.LotId });
        }
    }

    private async Task<ProductDetailDto> CreateKitAsync(World w, string sku, params (string Sku, decimal Qty)[] pieces)
    {
        using var s = Session(w.TenantId);
        return await s.GetRequiredService<AssembledKitService>().CreateAsync(new ProductCreateRequest(sku, sku, IsAssembledKit: true,
            KitPieces: pieces.Select(p => new AssembledKitPieceInput(w.Products[p.Sku].PublicId, p.Qty)).ToList()), default);
    }

    private async Task<decimal> TotalValueAsync(World w)
    {
        using var s = Session(w.TenantId);
        return (await s.GetRequiredService<ValuationService>().ReportAsync(null, null, null, 0, 500, default)).TotalValue;
    }

    private async Task<decimal> OnHandAsync(World w, int productId)
    {
        await using var db = fx.NewDb(w.TenantId);
        return await db.StockBalances.Where(b => b.ProductId == productId).SumAsync(b => b.QtyOnHand);
    }

    [Fact]
    public async Task Assembling_and_disassembling_on_sql_server_translate_and_keep_stock_lots_and_value()
    {
        if (fx.ConnectionString is null) return;
        var w = await SeedAsync("kit-armado-sql", 10m);
        var kit = await CreateKitAsync(w, "KIT-1", ("A", 2m), ("B", 1m));
        Assert.Equal((true, TrackingTypes.Lot), (kit.Product.IsAssembledKit, kit.Product.TrackingTypeCode));
        var before = await TotalValueAsync(w);

        KitAssemblyResultDto first;
        using (var s = Session(w.TenantId))
            first = await s.GetRequiredService<AssembledKitService>().AssembleAsync(kit.Product.PublicId, new KitAssembleRequest(2m, w.KitBin), default);
        Assert.Equal(("ARM-00001", "ARM-00001", (DateOnly?)new DateOnly(2027, 1, 15), 10m), (first.Assembly.Number, first.Assembly.KitLotNumber, first.Assembly.KitLotExpiry, first.Assembly.UnitCost));
        Assert.Equal(new[] { ("A", "A2", 4m), ("B", (string?)null, 2m) }, first.Assembly.Lines.Select(l => (l.Sku, l.LotNumber, l.Quantity)).ToArray());
        Assert.Equal(before, await TotalValueAsync(w));

        using (var s = Session(w.TenantId))
        {
            var svc = s.GetRequiredService<AssembledKitService>();
            var ex = await Assert.ThrowsAsync<InsufficientStockException>(() => svc.AssembleAsync(kit.Product.PublicId,
                new KitAssembleRequest(6m, w.KitBin, Pieces: new[] { new AssemblyPieceChoice(w.Products["A"].PublicId, LotId: w.Lots["A1"]) }), default));
            Assert.Equal("El lote A1 de A solo alcanza para 5 kit(s): hay 10 disponible y cada kit lleva 2. Arme 5 con ese lote y el resto en otro armado con otro lote.", ex.Message);
        }
        using (var s = Session(w.TenantId))
        {
            var dto = await s.GetRequiredService<AssembledKitService>().GetAsync(kit.Product.PublicId, default);
            Assert.Equal((2m, 2m, true), (dto.QtyOnHand, dto.QtyAvailable, dto.CompositionLocked));
            Assert.Equal((5, 5), (dto.Availability[0].CanAssemble, dto.Availability[0].CanAssembleTotal));   // A2 ya se usó: A1 (10 / 2) limita; B alcanza para 8
        }

        KitDisassemblyResultDto dis;
        using (var s = Session(w.TenantId))
            dis = await s.GetRequiredService<AssembledKitService>().DisassembleAsync(kit.Product.PublicId, first.Assembly.PublicId, new KitDisassembleRequest(1m), default);
        Assert.Equal(("DES-00001", 1m), (dis.Disassembly.Number, dis.Assembly.Remaining));
        Assert.Equal(12m, await OnHandAsync(w, w.Products["A"].ProductId));   // 14 − 4 + 2
        Assert.Equal(9m, await OnHandAsync(w, w.Products["B"].ProductId));
        Assert.Equal(1m, await OnHandAsync(w, kit.Product.Id));
        Assert.Equal(before, await TotalValueAsync(w));

        using (var s = Session(w.TenantId))
        {
            var page = await s.GetRequiredService<AssembledKitService>().ListAssembliesAsync(kit.Product.PublicId, 0, 50, default);
            Assert.Equal("DES-00001", Assert.Single(Assert.Single(page.Items).Disassemblies).Number);
        }
        // el Kárdex muestra los dos lados con su motivo y nombra el documento de origen
        Assert.All(first.Movements.Transactions, t => Assert.Equal((AdjustmentReasons.KitAssembly, "Armado de kit ARM-00001"), (t.ReasonCode, t.RefLabel)));
        Assert.Equal(new[] { -4m, -2m, 2m }, first.Movements.Transactions.Select(t => t.SignedQuantity).OrderBy(q => q).ToArray());
        Assert.All(dis.Movements.Transactions, t => Assert.Equal((AdjustmentReasons.KitDisassembly, "Desarmado de kit DES-00001"), (t.ReasonCode, t.RefLabel)));
        using (var s = Session(w.TenantId))
        {
            var detail = await s.GetRequiredService<InventoryReadService>().TransactionDetailAsync(first.Movements.Transactions[0].Id, InventoryScope.Any, default);
            Assert.Equal(("ARM-00001", "KIT-1 · W1"), (detail.Document!.Number, detail.Document.Reference));
            Assert.Equal(3, detail.Related.Count);
        }
    }

    [Theory]
    [InlineData(ValuationMethods.Average, 9)]   // promedio de las capas (5 a 2 y 5 a 4) = 3 → 3 B = 9
    [InlineData(ValuationMethods.Layers, 6)]    // por capas (FIFO): salen las 3 más viejas, a 2 = 6
    public async Task The_kit_costs_what_its_pieces_were_worth_and_the_total_value_does_not_change(string method, decimal expectedCost)
    {
        if (fx.ConnectionString is null) return;
        var w = await SeedAsync("kit-armado-valor-" + method, 10m, method, new[] { (5m, 2m), (5m, 4m) });
        var kit = await CreateKitAsync(w, "KIT-B", ("B", 3m));
        var before = await TotalValueAsync(w);
        KitAssemblyResultDto r;
        using (var s = Session(w.TenantId))
            r = await s.GetRequiredService<AssembledKitService>().AssembleAsync(kit.Product.PublicId, new KitAssembleRequest(1m, w.KitBin), default);
        Assert.Equal(expectedCost, r.Assembly.TotalCost);
        Assert.Equal(before, await TotalValueAsync(w));
        using (var s = Session(w.TenantId))
            await s.GetRequiredService<AssembledKitService>().DisassembleAsync(kit.Product.PublicId, r.Assembly.PublicId, new KitDisassembleRequest(1m), default);
        Assert.Equal(before, await TotalValueAsync(w));
    }

    [Fact]
    public async Task Two_assemblies_at_once_that_share_a_piece_never_leave_negative_stock()
    {
        if (fx.ConnectionString is null) return;
        var w = await SeedAsync("kit-armado-concurrencia", 7m);   // 7 B: alcanzan para 2 armados de 3, no para 4
        var k1 = await CreateKitAsync(w, "KIT-1", ("B", 3m));
        var k2 = await CreateKitAsync(w, "KIT-2", ("B", 3m));
        var kits = new[] { k1, k2, k1, k2 };
        var results = await Task.WhenAll(kits.Select(k => Task.Run(async () =>
        {
            using var s = Session(w.TenantId);
            try
            {
                await s.GetRequiredService<AssembledKitService>().AssembleAsync(k.Product.PublicId, new KitAssembleRequest(1m, w.KitBin), default);
                return "ok";
            }
            catch (InsufficientStockException) { return "409"; }
        })));
        Assert.Equal(2, results.Count(r => r == "ok"));
        Assert.Equal(2, results.Count(r => r == "409"));
        Assert.Equal(1m, await OnHandAsync(w, w.Products["B"].ProductId));
        Assert.Equal(2m, await OnHandAsync(w, k1.Product.Id) + await OnHandAsync(w, k2.Product.Id));
        await using var db = fx.NewDb(w.TenantId);
        Assert.False(await db.StockBalances.AnyAsync(b => b.QtyOnHand < 0m || b.QtyReserved > b.QtyOnHand));
        Assert.Equal(2, await db.Set<KitAssembly>().CountAsync());
    }

    [Fact]
    public async Task Another_company_on_sql_server_sees_nothing()
    {
        if (fx.ConnectionString is null) return;
        var w = await SeedAsync("kit-armado-a", 5m);
        var kit = await CreateKitAsync(w, "KIT-1", ("B", 1m));
        using (var s = Session(w.TenantId))
            await s.GetRequiredService<AssembledKitService>().AssembleAsync(kit.Product.PublicId, new KitAssembleRequest(1m, w.KitBin), default);
        var other = await fx.NewTenantAsync("kit-armado-b");
        using var o = Session(other);
        var svc = o.GetRequiredService<AssembledKitService>();
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync(kit.Product.PublicId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.AssembleAsync(kit.Product.PublicId, new KitAssembleRequest(1m, w.KitBin), default));
        var odb = o.GetRequiredService<TeikemDbContext>();
        Assert.Empty(await odb.Set<KitAssembly>().ToListAsync());
        Assert.Empty(await odb.Set<KitAssemblyLine>().ToListAsync());
    }
}
