using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 232 — KIT ARMADO en InMemory con el InventoryLedger REAL (WmsFixture): alta con composición (rastreo por las piezas), armar (salen las piezas, entran los kits,
/// nada reservado, costo = suma, valor total igual), lotes (el lote del kit recuerda los de las piezas, vence con el menor, un lote por pieza y el error dice cuántos
/// kits alcanzan), desarmar (vuelve a su lote, parcial, no si están reservados), vender como producto normal, composición inmutable con historia, permisos y aislamiento.
/// El bloqueo real y la concurrencia se prueban contra SQL Server en AssembledKitSqlTests.
/// </summary>
public sealed class AssembledKitTests
{
    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin P1, WarehouseBin P2, WarehouseBin P3, WarehouseBin K1, WarehouseBin Q1,
        Product A, Product B, Product C, InventoryLot A1, InventoryLot A2, InventoryLot C1)
    {
        public AssembledKitService Kits => F.Get<AssembledKitService>();
    }

    private static readonly DateOnly ExpA1 = new(2027, 3, 1);
    private static readonly DateOnly ExpA2 = new(2027, 1, 15);
    private static readonly DateOnly ExpC1 = new(2026, 12, 31);

    /// <summary>
    /// A (lote, costo 3): A1 vence 2027-03-01 con 10 en P-01 y A2 vence 2027-01-15 con 4 en P-02. B (sin lote, costo 4): 10 en P-03 y 5 en cuarentena (Q-01).
    /// C (lote, costo 1): C1 vence 2026-12-31 con 10 en P-01. K-01 recibe los kits.
    /// </summary>
    private static async Task<World> SeedAsync(Action<IServiceCollection>? extra = null, bool usesAssembledKits = true)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<InventoryReadService>();
            s.AddSingleton<ProductService>();
            s.AddSingleton<ValuationService>();
            s.AddSingleton<AssembledKitService>();
            extra?.Invoke(s);
        });
        // Lote 233: el alta de un kit armado pide el ajuste de la compañía «trabaja con kits armados» (apagado por omisión); estas pruebas lo encienden.
        if (usesAssembledKits)
        {
            (await f.Db.Tenants.SingleAsync(t => t.TenantId == WmsFixture.TenantId)).UsesAssembledKits = true;
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
        }
        var w = await f.AddWarehouseAsync("W1");
        var pck = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var p1 = await f.AddBinAsync(pck, "P-01");
        var p2 = await f.AddBinAsync(pck, "P-02");
        var p3 = await f.AddBinAsync(pck, "P-03");
        var k1 = await f.AddBinAsync(pck, "K-01");
        var q1 = await f.AddBinAsync(await f.AddZoneAsync(w, "CUA", ZoneTypes.Quarantine), "Q-01");
        var a = await f.AddProductAsync("A", TrackingTypes.Lot, purchaseCost: 3m);
        var b = await f.AddProductAsync("B", purchaseCost: 4m);
        var c = await f.AddProductAsync("C", TrackingTypes.Lot, purchaseCost: 1m);
        var a1 = await f.AddLotAsync(a, "A1", ExpA1);
        var a2 = await f.AddLotAsync(a, "A2", ExpA2);
        var c1 = await f.AddLotAsync(c, "C1", ExpC1);
        InventoryPosting In(Product p, decimal q, WarehouseBin bin, InventoryLot? lot = null)
            => new(InventoryTxnTypes.Adjustment, p.ProductId, q, LotId: lot?.LotId, ToWarehouseId: w.WarehouseId, ToBinId: bin.WarehouseBinId, ReasonCode: AdjustmentReasons.Found, Notes: "saldo");
        await f.PostAsync(In(a, 10m, p1, a1), In(a, 4m, p2, a2), In(b, 10m, p3), In(b, 5m, q1), In(c, 10m, p1, c1));
        return new World(f, w, p1, p2, p3, k1, q1, a, b, c, a1, a2, c1);
    }

    private static async Task<T> Run<T>(World w, Func<Task<T>> act)
    {
        try { return await act(); }
        finally { w.F.Db.ChangeTracker.Clear(); }
    }

    private static AssembledKitPieceInput Piece(Product p, decimal q) => new(p.PublicId, q);

    /// <summary>Kit = 2 A + 1 B + 1 C (por lote: A y C llevan lote).</summary>
    private static Task<ProductDetailDto> CreateKitAsync(World w, string sku = "KIT-1", params AssembledKitPieceInput[] pieces)
        => Run(w, () => w.Kits.CreateAsync(new ProductCreateRequest(sku, "Kit " + sku, IsAssembledKit: true,
            KitPieces: pieces.Length > 0 ? pieces : new[] { Piece(w.A, 2m), Piece(w.B, 1m), Piece(w.C, 1m) }), default));

    private static Task<KitAssemblyResultDto> AssembleAsync(World w, Guid kit, decimal qty, WarehouseBin? to = null, params AssemblyPieceChoice[] choices)
        => Run(w, () => w.Kits.AssembleAsync(kit, new KitAssembleRequest(qty, (to ?? w.K1).WarehouseBinId, Pieces: choices.Length == 0 ? null : choices), default));

    private static async Task<decimal> OnHandAsync(World w, Product p, WarehouseBin bin, InventoryLot? lot = null) => await w.F.OnHandAsync(p.ProductId, bin.WarehouseBinId, lot?.LotId);

    private static async Task<decimal> TotalValueAsync(World w) => (await Run(w, () => w.F.Get<ValuationService>().ReportAsync(null, null, null, 0, 500, default))).TotalValue;

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    // ================================================================ alta

    [Fact]
    public async Task Creating_an_assembled_kit_saves_its_composition_and_lot_tracking_decided_by_the_pieces()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var kit = await CreateKitAsync(w);
        Assert.True(kit.Product.IsAssembledKit);
        Assert.False(kit.Product.IsKit);
        Assert.Equal(TrackingTypes.Lot, kit.Product.TrackingTypeCode);   // A y C van por lote
        Assert.Equal(new[] { ("A", 2m), ("B", 1m), ("C", 1m) }, kit.KitPieces!.Select(p => (p.Sku, p.Quantity)).ToArray());

        var list = await Run(w, () => w.F.Get<ProductService>().ListAsync(new ProductListQuery(Search: "KIT-1"), InventoryScope.Any, default));
        Assert.True(Assert.Single(list.Items).IsAssembledKit);

        // sin piezas con lote, el kit no se rastrea por lote
        var plain = await CreateKitAsync(w, "KIT-B", Piece(w.B, 3m));
        Assert.Equal(TrackingTypes.None, plain.Product.TrackingTypeCode);
        var dto = await Run(w, () => w.Kits.GetAsync(plain.Product.PublicId, default));
        Assert.True(dto.IsAssembledKit);
        Assert.False(dto.CompositionLocked);
        Assert.Equal(3, Assert.Single(dto.Availability).CanAssemble);   // 10 B disponibles (la cuarentena no cuenta) / 3
    }

    [Fact]
    public async Task Creation_rejects_bad_pieces_and_a_tracking_that_contradicts_the_pieces()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var serial = await w.F.AddProductAsync("S", TrackingTypes.Serial);
        var other = await w.F.AddProductAsync("O", ownerClientId: (await w.F.AddClientAsync("CL")).ClientId);
        var inactive = await w.F.AddProductAsync("I", isActive: false);
        var kitA = await CreateKitAsync(w, "KIT-A", Piece(w.B, 1m));

        async Task<string> Fails(ProductCreateRequest req) => await MsgOf(() => Run(w, () => w.Kits.CreateAsync(req, default)));
        ProductCreateRequest Req(params AssembledKitPieceInput[] p) => new("NEW", "Nuevo", IsAssembledKit: true, KitPieces: p);
        Assert.Equal(AssembledKitRules.PieceIsSerial("S"), await Fails(Req(Piece(serial, 1m))));
        Assert.Equal(AssembledKitRules.PieceIsKit("KIT-A"), await Fails(Req(new AssembledKitPieceInput(kitA.Product.PublicId, 1m))));
        Assert.Equal(KitRules.ComponentOtherOwner("O"), await Fails(Req(Piece(other, 1m))));
        Assert.Equal(KitRules.ComponentInactive("I"), await Fails(Req(Piece(inactive, 1m))));
        Assert.Equal(KitRules.ComponentQuantity, await Fails(Req(Piece(w.B, 0m))));
        Assert.Equal(KitRules.DuplicateComponent("B"), await Fails(Req(Piece(w.B, 1m), Piece(w.B, 2m))));
        Assert.Equal(KitRules.NeedComponents, await Fails(new ProductCreateRequest("NEW", "Nuevo", IsAssembledKit: true)));
        Assert.Equal(AssembledKitRules.TrackingDecidedByPieces(TrackingTypes.Lot),
            await Fails(new ProductCreateRequest("NEW", "Nuevo", TrackingType: "NONE", IsAssembledKit: true, KitPieces: new[] { Piece(w.A, 1m) })));
        Assert.Equal(AssembledKitRules.AssembleOnCreateNeedsBin,
            await Fails(new ProductCreateRequest("NEW", "Nuevo", IsAssembledKit: true, KitPieces: new[] { Piece(w.B, 1m) }, AssembleQuantity: 1m)));
        Assert.False(await w.F.Db.Products.AnyAsync(p => p.Sku == "NEW"));   // nada se creó
    }

    [Fact]
    public async Task Creating_with_kits_to_assemble_leaves_them_in_stock()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var kit = await Run(w, () => w.Kits.CreateAsync(new ProductCreateRequest("KIT-1", "Kit", IsAssembledKit: true, KitPieces: new[] { Piece(w.B, 2m) },
            AssembleQuantity: 3m, AssembleToBinId: w.K1.WarehouseBinId), default));
        Assert.Equal(3m, kit.Product.QtyOnHand);
        Assert.Equal(4m, await OnHandAsync(w, w.B, w.P3));
        var history = await Run(w, () => w.Kits.ListAssembliesAsync(kit.Product.PublicId, 0, 50, default));
        Assert.Equal("ARM-00001", Assert.Single(history.Items).Number);
    }

    // ================================================================ armar

    [Fact]
    public async Task Assembling_moves_the_pieces_out_and_the_kits_in_nothing_reserved_cost_is_the_sum_and_the_total_value_does_not_change()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var kit = await CreateKitAsync(w);
        var valueBefore = await TotalValueAsync(w);
        Assert.Equal(42m + 60m + 10m, valueBefore);   // A 14×3, B 15×4 (la cuarentena cuenta), C 10×1

        var r = await AssembleAsync(w, kit.Product.PublicId, 2m);
        var a = r.Assembly;
        Assert.Equal(("ARM-00001", 2m, 0m, 2m), (a.Number, a.Quantity, a.DisassembledQty, a.Remaining));
        Assert.Equal((11m, 22m), (a.UnitCost, a.TotalCost));   // 2×3 + 4 + 1

        // salen las piezas (FEFO: A2 vence antes que A1) y entran los kits a K-01 en su lote ARM-00001
        Assert.Equal(0m, await OnHandAsync(w, w.A, w.P2, w.A2));
        Assert.Equal(10m, await OnHandAsync(w, w.A, w.P1, w.A1));
        Assert.Equal(8m, await OnHandAsync(w, w.B, w.P3));
        Assert.Equal(5m, await OnHandAsync(w, w.B, w.Q1));   // la cuarentena no se toca
        Assert.Equal(8m, await OnHandAsync(w, w.C, w.P1, w.C1));
        var kitLot = await w.F.Db.InventoryLots.AsNoTracking().SingleAsync(l => l.ProductId == kit.Product.Id);
        Assert.Equal(("ARM-00001", (DateOnly?)ExpC1), (kitLot.LotNumber, kitLot.ExpiryDate));   // vence con la pieza que vence primero
        Assert.Equal(2m, await w.F.OnHandAsync(kit.Product.Id, w.K1.WarehouseBinId, kitLot.LotId));

        // nada queda reservado: son movimientos reales
        Assert.All(await w.F.Db.StockBalances.AsNoTracking().ToListAsync(), b => Assert.Equal(0m, b.QtyReserved));
        Assert.Equal(valueBefore, await TotalValueAsync(w));

        // Kárdex: los dos lados con el motivo «Armado de kit» y el armado como origen
        var reason = w.F.LookupId(LookupDomains.AdjustmentReason, AdjustmentReasons.KitAssembly);
        var origin = w.F.LookupId(LookupDomains.EntityType, AssembledKitCodes.AssemblyEntityType);
        var txns = (await w.F.TransactionsAsync()).Where(t => t.ReasonLookupId == reason).ToList();
        Assert.Equal(4, txns.Count);   // A, B, C salen; el kit entra
        Assert.All(txns, t => Assert.Equal((origin, "Armado de kit KIT-1 · ARM-00001"), (t.RefEntityLookupId!.Value, t.Notes)));
        Assert.Equal(2m, txns.Single(t => t.ProductId == kit.Product.Id).Quantity);
        Assert.Equal(-4m, txns.Single(t => t.ProductId == w.A.ProductId).Quantity);
        Assert.Equal(4, r.Movements.Balances.Count);   // saldos tocados: A2, B, C1 y el kit
        Assert.Equal(4, r.Movements.Transactions.Count);
    }

    [Fact]
    public async Task A_lot_that_is_not_enough_says_how_many_kits_it_covers_and_another_lot_can_be_chosen()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var kit = await CreateKitAsync(w);
        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() => AssembleAsync(w, kit.Product.PublicId, 3m));
        Assert.Equal("El lote A2 de A solo alcanza para 2 kit(s): hay 4 disponible y cada kit lleva 2. Arme 2 con ese lote y el resto en otro armado con otro lote.", ex.Message);
        Assert.Equal(4m, await OnHandAsync(w, w.A, w.P2, w.A2));   // no se movió nada
        Assert.False(await w.F.Db.Set<KitAssembly>().AnyAsync());

        // escogiendo A1: un solo lote por pieza, y el lote del kit recuerda el lote y la cantidad de cada pieza
        var r = await AssembleAsync(w, kit.Product.PublicId, 3m, null, new AssemblyPieceChoice(w.A.PublicId, LotId: w.A1.LotId));
        Assert.Equal(new (string, string?, decimal)[] { ("A", "A1", 6m), ("B", null, 3m), ("C", "C1", 3m) },
            r.Assembly.Lines.Select(l => (l.Sku, l.LotNumber, l.Quantity)).ToArray());
        Assert.Equal(ExpC1, r.Assembly.KitLotExpiry);
        Assert.Equal(4m, await OnHandAsync(w, w.A, w.P1, w.A1));

        // un lote que no alcanza para ningún kit
        var none = await Assert.ThrowsAsync<InsufficientStockException>(() => AssembleAsync(w, kit.Product.PublicId, 1m, null, new AssemblyPieceChoice(w.C.PublicId, LotId: w.C1.LotId),
            new AssemblyPieceChoice(w.A.PublicId, LotId: w.A1.LotId, BinId: w.P2.WarehouseBinId)));
        Assert.Equal("El lote A1 de A no alcanza para ningún kit: hay 0 disponible y cada kit lleva 2.", none.Message);
    }

    [Fact]
    public async Task Assembling_uses_only_available_stock_and_validates_the_request()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var kit = await CreateKitAsync(w, "KIT-B", Piece(w.B, 4m));
        // 10 B disponibles en P-03 (los 5 de cuarentena no cuentan): para 3 kits faltan
        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() => AssembleAsync(w, kit.Product.PublicId, 3m));
        Assert.Equal("No hay suficiente B disponible en W1 para 3 kit(s): hay 10 y se necesitan 12; alcanza para 2.", ex.Message);
        // lo reservado tampoco cuenta
        await w.F.ReserveAsync(new StockReservation(w.B.ProductId, w.W.WarehouseId, w.P3.WarehouseBinId, null, 3m));
        Assert.Contains("hay 7", (await Assert.ThrowsAsync<InsufficientStockException>(() => AssembleAsync(w, kit.Product.PublicId, 2m))).Message);

        Assert.Equal(AssembledKitRules.KitQuantityInvalid, await MsgOf(() => AssembleAsync(w, kit.Product.PublicId, 1.5m)));
        Assert.Equal(AssembledKitRules.BinNotAllowed("Q-01"), await MsgOf(() => AssembleAsync(w, kit.Product.PublicId, 1m, w.Q1)));
        Assert.Equal(AssembledKitRules.PieceNotInKit, await MsgOf(() => AssembleAsync(w, kit.Product.PublicId, 1m, null, new AssemblyPieceChoice(w.A.PublicId))));
        Assert.Equal(AssembledKitRules.NotAssembledKit("A"), await MsgOf(() => AssembleAsync(w, w.A.PublicId, 1m)));

        // sin piezas con lote: el kit no lleva lote, pero el armado queda registrado con su detalle
        var r = await AssembleAsync(w, kit.Product.PublicId, 1m);
        Assert.Null(r.Assembly.KitLotId);
        Assert.Equal(("B", 4m), (Assert.Single(r.Assembly.Lines).Sku, r.Assembly.Lines[0].Quantity));
        Assert.Equal(1m, await w.F.OnHandAsync(kit.Product.Id, w.K1.WarehouseBinId));
    }

    [Fact]
    public async Task Assembling_and_disassembling_need_inventory_adjust()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var kit = await CreateKitAsync(w, "KIT-B", Piece(w.B, 1m));
        w.F.SetPermissions(PermissionCatalog.InventoryView, PermissionCatalog.InventoryManage);
        await Assert.ThrowsAsync<ForbiddenException>(() => AssembleAsync(w, kit.Product.PublicId, 1m));
        await Assert.ThrowsAsync<ForbiddenException>(() => Run(w, () => w.Kits.DisassembleAsync(kit.Product.PublicId, Guid.NewGuid(), new KitDisassembleRequest(1m), default)));
        await Assert.ThrowsAsync<ForbiddenException>(() => Run(w, () => w.Kits.CreateAsync(new ProductCreateRequest("K2", "K2", IsAssembledKit: true,
            KitPieces: new[] { Piece(w.B, 1m) }, AssembleQuantity: 1m, AssembleToBinId: w.K1.WarehouseBinId), default)));
    }

    // ================================================================ desarmar

    [Fact]
    public async Task Disassembling_returns_each_piece_to_its_lot_partially_and_never_reserved_kits()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var kit = await CreateKitAsync(w);
        var valueBefore = await TotalValueAsync(w);
        var a = (await AssembleAsync(w, kit.Product.PublicId, 2m)).Assembly;
        var kitLot = a.KitLotId!.Value;

        var one = await Run(w, () => w.Kits.DisassembleAsync(kit.Product.PublicId, a.PublicId, new KitDisassembleRequest(1m), default));
        Assert.Equal(("DES-00001", 1m), (one.Disassembly.Number, one.Disassembly.Quantity));
        Assert.Equal((1m, 1m), (one.Assembly.DisassembledQty, one.Assembly.Remaining));
        Assert.Equal(2m, await OnHandAsync(w, w.A, w.P2, w.A2));   // vuelve a su lote y a su posición
        Assert.Equal(9m, await OnHandAsync(w, w.B, w.P3));
        Assert.Equal(9m, await OnHandAsync(w, w.C, w.P1, w.C1));
        Assert.Equal(1m, await w.F.OnHandAsync(kit.Product.Id, w.K1.WarehouseBinId, kitLot));
        Assert.All(one.Disassembly.Lines, l => Assert.Equal(l.Sku == "A" ? 3m : l.Sku == "B" ? 4m : 1m, l.UnitCost));   // al costo con que salió

        Assert.Equal(AssembledKitRules.AssemblyRemaining("ARM-00001", 1m, 2m),
            await MsgOf(() => Run(w, () => w.Kits.DisassembleAsync(kit.Product.PublicId, a.PublicId, new KitDisassembleRequest(2m), default))));

        // un kit reservado (por una orden) no se desarma
        await w.F.ReserveAsync(new StockReservation(kit.Product.Id, w.W.WarehouseId, w.K1.WarehouseBinId, kitLot, 1m));
        var reserved = await Assert.ThrowsAsync<InsufficientStockException>(() => Run(w, () => w.Kits.DisassembleAsync(kit.Product.PublicId, a.PublicId, new KitDisassembleRequest(1m), default)));
        Assert.Equal(AssembledKitRules.KitsNotAvailable("ARM-00001", "W1", 0m, 1m), reserved.Message);
        await w.F.ReleaseAsync(new StockReservation(kit.Product.Id, w.W.WarehouseId, w.K1.WarehouseBinId, kitLot, 1m));

        // el último, con las piezas a otra posición
        var last = await Run(w, () => w.Kits.DisassembleAsync(kit.Product.PublicId, a.PublicId, new KitDisassembleRequest(1m, ToBinId: w.P3.WarehouseBinId), default));
        Assert.Equal(0m, last.Assembly.Remaining);
        Assert.Equal(2m, await OnHandAsync(w, w.A, w.P3, w.A2));
        Assert.Equal(1m, await OnHandAsync(w, w.C, w.P3, w.C1));
        Assert.Equal(0m, await w.F.OnHandAsync(kit.Product.Id, w.K1.WarehouseBinId, kitLot));
        Assert.Equal(valueBefore, await TotalValueAsync(w));

        // historial: el armado con sus dos desarmados
        var history = await Run(w, () => w.Kits.ListAssembliesAsync(kit.Product.PublicId, 0, 50, default));
        Assert.Equal(new[] { "DES-00001", "DES-00002" }, Assert.Single(history.Items).Disassemblies.Select(d => d.Number));
        Assert.All(history.Items[0].Lines, l => Assert.Equal(l.Quantity, l.ReturnedQty));
    }

    // ================================================================ vender

    private static void SalesServices(IServiceCollection s)
    {
        s.AddSingleton<TaxService>();
        s.AddSingleton<FakeBalance>();
        s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
        s.AddSingleton<SalesOrderService>();
        s.AddSingleton<SyncService>();
        s.AddSingleton<ISalesDispatchReversal>(sp => sp.GetRequiredService<SalesOrderService>());
    }

    [Fact]
    public async Task An_assembled_kit_is_sold_reserved_and_dispatched_like_any_product_without_exploding_nor_substituting()
    {
        var w = await SeedAsync(SalesServices);
        await using var _ = w.F;
        w.F.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.SalesOrders);
        var client = await w.F.AddClientAsync("C1");
        var kit = await CreateKitAsync(w);
        var row = await w.F.Db.Set<Product>().AsTracking().SingleAsync(p => p.ProductId == kit.Product.Id);
        row.SalePrice = 50m;
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        var kitLot = (await AssembleAsync(w, kit.Product.PublicId, 2m)).Assembly.KitLotId!.Value;
        var sales = w.F.Get<SalesOrderService>();

        var draft = await Run(w, () => sales.CreateAsync(new SalesOrderRequest(w.W.PublicId, client.PublicId, Lines: new[] { new SalesOrderLineRequest(kit.Product.PublicId, 2m) }), default));
        Assert.False(draft.Lines[0].IsKit);   // no se explota en piezas
        var o = await Run(w, () => sales.RequestAsync(draft.PublicId, null, default));
        Assert.Equal((2m, 0m), (o.Lines[0].QtyReserved, o.Lines[0].QtyBackordered));
        Assert.Equal(2m, (await w.F.BalanceAsync(kit.Product.Id, w.K1.WarehouseBinId, kitLot))!.QtyReserved);   // se reserva el kit, no sus piezas
        Assert.Equal(0m, (await w.F.BalanceAsync(w.B.ProductId, w.P3.WarehouseBinId))!.QtyReserved);

        // reservado: no se puede desarmar
        var assembly = (await Run(w, () => w.Kits.ListAssembliesAsync(kit.Product.PublicId, 0, 50, default))).Items[0];
        await Assert.ThrowsAsync<InsufficientStockException>(() => Run(w, () => w.Kits.DisassembleAsync(kit.Product.PublicId, assembly.PublicId, new KitDisassembleRequest(1m), default)));

        var d = await Run(w, () => sales.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, 2m) }), default));
        Assert.Equal("SHIPPED", d.Order.Status);
        Assert.Equal(0m, await w.F.OnHandAsync(kit.Product.Id, w.K1.WarehouseBinId, kitLot));
        Assert.Equal(8m, await OnHandAsync(w, w.B, w.P3));   // las piezas no se tocan al vender
        var line = await w.F.Db.Set<PickBatchLine>().AsNoTracking().SingleAsync();
        Assert.Equal((kit.Product.Id, (int?)kitLot, (int?)null), (line.ProductId, line.LotId, line.KitProductId));

        // sin existencia: falta y va a backorder, como cualquier producto
        var next = await Run(w, () => sales.CreateAsync(new SalesOrderRequest(w.W.PublicId, client.PublicId, Lines: new[] { new SalesOrderLineRequest(kit.Product.PublicId, 1m) }), default));
        await Assert.ThrowsAsync<InsufficientStockException>(() => Run(w, () => sales.RequestAsync(next.PublicId, null, default)));
        var back = await Run(w, () => sales.RequestAsync(next.PublicId, new SalesOrderRequestRequest(AllowBackorder: true), default));
        Assert.Equal((0m, 1m), (back.Lines[0].QtyReserved, back.Lines[0].QtyBackordered));

        // sin sustitución de piezas: no es un kit de oferta
        Assert.Equal(KitRules.NotAKit("KIT-1"), await MsgOf(() => Run(w, () => sales.SubstitutesAsync(kit.Product.PublicId, default))));
    }

    // ================================================================ composición

    [Fact]
    public async Task The_composition_changes_only_without_history_and_tracking_and_owner_are_fixed_by_the_pieces()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var kit = await CreateKitAsync(w);
        var changed = await Run(w, () => w.Kits.SetCompositionAsync(kit.Product.PublicId, new AssembledKitSetRequest(new[] { Piece(w.B, 2m) }), default));
        Assert.Equal((TrackingTypes.None, "B"), (changed.TrackingTypeCode, Assert.Single(changed.Pieces).Sku));

        var products = w.F.Get<ProductService>();
        Assert.Equal(AssembledKitRules.TrackingFixed, await MsgOf(() => Run(w, () => products.UpdateAsync(kit.Product.PublicId, new ProductPatchRequest(TrackingType: "LOT"), default))));
        var client = await w.F.AddClientAsync("CL");
        Assert.Equal(AssembledKitRules.OwnerFixed, await MsgOf(() => Run(w, () => products.UpdateAsync(kit.Product.PublicId, new ProductPatchRequest(OwnerClientPublicId: client.PublicId), default))));

        await AssembleAsync(w, kit.Product.PublicId, 1m);
        var dto = await Run(w, () => w.Kits.GetAsync(kit.Product.PublicId, default));
        Assert.True(dto.CompositionLocked);
        Assert.Equal(AssembledKitRules.CompositionLocked("KIT-1"),
            await MsgOf(() => Run(w, () => w.Kits.SetCompositionAsync(kit.Product.PublicId, new AssembledKitSetRequest(new[] { Piece(w.B, 3m) }), default))));
        Assert.Equal(AssembledKitRules.NotAssembledKit("A"),
            await MsgOf(() => Run(w, () => w.Kits.SetCompositionAsync(w.A.PublicId, new AssembledKitSetRequest(new[] { Piece(w.B, 3m) }), default))));
    }

    // ================================================================ Lote 233: el parámetro de la compañía «trabaja con kits armados»

    private static TenantSettingsUpdateRequest UsesKits(bool? value)
        => new(null, null, null, null, null, null, null, null, null, null, null) { UsesAssembledKits = value };

    [Fact]
    public async Task The_company_setting_is_off_by_default_and_creating_an_assembled_kit_is_rejected_until_it_is_turned_on()
    {
        var w = await SeedAsync(s => s.AddSingleton<TenantService>(), usesAssembledKits: false);
        await using var _ = w.F;
        var settings = w.F.Get<TenantService>();
        Assert.False(new Teikem.Domain.Tenancy.Tenant().UsesAssembledKits);                       // por omisión: NO
        Assert.False((await Run(w, () => settings.GetSettingsAsync(default))).UsesAssembledKits);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => CreateKitAsync(w));           // 422
        Assert.Equal("La compañía no trabaja con kits armados: actívelo en Ajustes → Operación.", ex.Message);
        Assert.Equal(AssembledKitRules.CompanyDoesNotUseAssembledKits, ex.Message);
        Assert.Empty(await w.F.Db.Set<Product>().AsNoTracking().Where(p => p.Sku == "KIT-1").ToListAsync());   // no quedó nada

        // un cambio parcial que no lo trae no lo toca; encenderlo permite el alta
        Assert.False((await Run(w, () => settings.UpdateSettingsAsync(UsesKits(null), default))).UsesAssembledKits);
        Assert.True((await Run(w, () => settings.UpdateSettingsAsync(UsesKits(true), default))).UsesAssembledKits);
        Assert.True((await Run(w, () => settings.GetSettingsAsync(default))).UsesAssembledKits);
        var kit = await CreateKitAsync(w);
        Assert.True(kit.Product.IsAssembledKit);
    }

    [Fact]
    public async Task Turning_the_setting_off_keeps_existing_assembled_kits_working_but_stops_new_ones()
    {
        var w = await SeedAsync(s => s.AddSingleton<TenantService>());
        await using var _ = w.F;
        var kit = await CreateKitAsync(w);
        var first = (await AssembleAsync(w, kit.Product.PublicId, 1m)).Assembly;
        Assert.False((await Run(w, () => w.F.Get<TenantService>().UpdateSettingsAsync(UsesKits(false), default))).UsesAssembledKits);

        // los que ya existen: se leen, se arman y se desarman igual
        Assert.True((await Run(w, () => w.Kits.GetAsync(kit.Product.PublicId, default))).IsAssembledKit);
        var second = (await AssembleAsync(w, kit.Product.PublicId, 1m)).Assembly;
        Assert.Equal(1m, second.Quantity);
        var back = await Run(w, () => w.Kits.DisassembleAsync(kit.Product.PublicId, first.PublicId, new KitDisassembleRequest(1m), default));
        Assert.Equal(0m, back.Assembly.Remaining);
        // pero no se crean kits nuevos
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => CreateKitAsync(w, "KIT-2"));
        Assert.Equal(AssembledKitRules.CompanyDoesNotUseAssembledKits, ex.Message);
        // y un producto normal se crea como siempre
        var plain = await Run(w, () => w.F.Get<ProductService>().CreateAsync(new ProductCreateRequest("NORMAL-1", "Producto normal"), default));
        Assert.False(plain.Product.IsAssembledKit);
    }

    [Fact]
    public void Update_sql_adds_the_setting_off_by_default_in_an_idempotent_section_at_the_end()
    {
        var sql = File.ReadAllText(Path.Combine(Teikem.Infrastructure.Persistence.Scripts.DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-11-10 — Lote 233: parámetro de la compañía «La compañía trabaja con kits armados»", StringComparison.Ordinal);
        Assert.True(start > sql.IndexOf("2026-11-10 — Lote 232: KIT ARMADO", StringComparison.Ordinal));
        var section = UpdateSqlSection.From(sql, start);
        Assert.Contains("IF COL_LENGTH(N'dbo.Tenant', N'UsesAssembledKits') IS NULL", section);
        Assert.Contains("ALTER TABLE dbo.Tenant ADD UsesAssembledKits BIT NOT NULL CONSTRAINT DF_Tenant_UsesAssembledKits DEFAULT 0;", section);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP", section, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ aislamiento

    [Fact]
    public async Task Another_company_does_not_see_nor_assemble_the_kit()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var kit = await CreateKitAsync(w);
        var a = (await AssembleAsync(w, kit.Product.PublicId, 1m)).Assembly;
        using (w.F.AsTenant(WmsFixture.OtherTenantId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => Run(w, () => w.Kits.GetAsync(kit.Product.PublicId, default)));
            await Assert.ThrowsAsync<NotFoundException>(() => Run(w, () => w.Kits.ListAssembliesAsync(kit.Product.PublicId, 0, 50, default)));
            await Assert.ThrowsAsync<NotFoundException>(() => AssembleAsync(w, kit.Product.PublicId, 1m));
            await Assert.ThrowsAsync<NotFoundException>(() => Run(w, () => w.Kits.DisassembleAsync(kit.Product.PublicId, a.PublicId, new KitDisassembleRequest(1m), default)));
            Assert.Empty(await w.F.Db.Set<KitAssembly>().AsNoTracking().ToListAsync());
            Assert.Empty(await w.F.Db.Set<KitAssemblyLine>().AsNoTracking().ToListAsync());
        }
    }

    // ================================================================ inventario normal

    [Fact]
    public async Task An_assembled_kit_is_transferred_and_adjusted_like_any_lot_product_and_its_system_reasons_are_not_manual()
    {
        var w = await SeedAsync(s => s.AddSingleton<InventoryAdjustmentService>());
        await using var _ = w.F;
        var kit = await CreateKitAsync(w);
        var kitLot = (await AssembleAsync(w, kit.Product.PublicId, 2m)).Assembly.KitLotId!.Value;
        var adjust = w.F.Get<InventoryAdjustmentService>();

        // transferir: el guard «un kit no tiene existencia propia» es solo del tipo KIT
        await Run(w, () => adjust.TransferAsync(new TransferRequest(kit.Product.PublicId, w.K1.WarehouseBinId, w.P3.WarehouseBinId, 1m, LotId: kitLot), default));
        Assert.Equal(1m, await w.F.OnHandAsync(kit.Product.Id, w.K1.WarehouseBinId, kitLot));
        Assert.Equal(1m, await w.F.OnHandAsync(kit.Product.Id, w.P3.WarehouseBinId, kitLot));

        // ajuste manual (daño) del kit, con su lote
        await Run(w, () => adjust.AdjustAsync(new AdjustmentRequest(kit.Product.PublicId, w.W.PublicId, w.P3.WarehouseBinId, -1m, AdjustmentReasons.Damage, "Se rompió", LotId: kitLot), default));
        Assert.Equal(0m, await w.F.OnHandAsync(kit.Product.Id, w.P3.WarehouseBinId, kitLot));

        // los motivos del armado los asigna el sistema
        var sys = await Assert.ThrowsAsync<ValidationException>(() => Run(w, () => adjust.AdjustAsync(
            new AdjustmentRequest(w.B.PublicId, w.W.PublicId, w.P3.WarehouseBinId, -1m, AdjustmentReasons.KitAssembly, "x"), default)));
        Assert.Equal(AdjustmentRules.SystemReason(AdjustmentReasons.KitAssembly), sys.Errors!["reason"][0]);
    }

    [Fact]
    public async Task The_product_import_updates_an_assembled_kit_like_any_product_but_not_its_tracking()
    {
        var w = await SeedAsync(s =>
        {
            s.AddSingleton<ImportTemplateService>();
            s.AddSingleton<EntityImportService>();
            s.AddSingleton<IImportEntityRows, ProductImportRows>();
        });
        await using var _ = w.F;
        var kit = await CreateKitAsync(w);
        var tpl = await Run(w, () => w.F.Get<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest("Productos",
            [new(1, "sku"), new(2, "name"), new(3, "salePrice"), new(4, "trackingType")], null), Teikem.Domain.Orders.ImportKinds.Product, default));
        var imports = w.F.Get<EntityImportService>();
        byte[] Csv(params string[] lines) => System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines));

        var ok = await Run(w, () => imports.ImportAsync(Teikem.Domain.Orders.ImportKinds.Product, tpl.PublicId, Csv("sku,name,price,tracking", "KIT-1,Kit renombrado,50,LOT"), "p.csv", commit: true, default));
        Assert.NotEqual("ERROR", ok.Rows[0].Status);
        var row = await w.F.Db.Products.AsNoTracking().SingleAsync(p => p.ProductId == kit.Product.Id);
        Assert.Equal(("Kit renombrado", (decimal?)50m), (row.Name, row.SalePrice));

        var bad = await Run(w, () => imports.ImportAsync(Teikem.Domain.Orders.ImportKinds.Product, tpl.PublicId, Csv("sku,name,price,tracking", "KIT-1,Otro,50,NONE"), "p.csv", commit: true, default));
        Assert.Equal("ERROR", bad.Rows[0].Status);
        Assert.Contains(AssembledKitRules.TrackingFixed, bad.Rows[0].Errors.Values);
    }

    // ================================================================ reglas puras

    [Fact]
    public void Pure_rules_decide_tracking_expiry_and_how_many_kits_fit()
    {
        Assert.Equal(TrackingTypes.Lot, AssembledKitRules.DecideTracking(new[] { "NONE", "LOT" }));
        Assert.Equal(TrackingTypes.None, AssembledKitRules.DecideTracking(new[] { "NONE" }));
        Assert.Equal(new DateOnly(2026, 1, 1), AssembledKitRules.KitExpiry(new DateOnly?[] { null, new(2026, 5, 1), new(2026, 1, 1) }));
        Assert.Null(AssembledKitRules.KitExpiry(new DateOnly?[] { null }));
        Assert.Equal(2, AssembledKitRules.KitsCoveredBy(5m, 2m));
        Assert.Equal(0, AssembledKitRules.KitsCoveredBy(-1m, 2m));
        Assert.Equal(AssembledKitRules.KitQuantityRequired, AssembledKitRules.ValidateKitQuantity(null));
        Assert.Equal(AssembledKitRules.KitQuantityInvalid, AssembledKitRules.ValidateKitQuantity(0m));
        Assert.NotNull(AssembledKitRules.ValidateKitQuantity(AssembledKitCodes.MaxKitsPerOperation + 1));
        Assert.Null(AssembledKitRules.ValidateKitQuantity(3m));
        // un lote por pieza: A tiene lotes de 5 y 3 (alcanza 2 kits en uno solo de 2 c/u); B sin lote 10 / 1
        Assert.Equal(2, AssembledKitRules.CanAssembleInOne(new (decimal, bool, IReadOnlyDictionary<int, decimal>, decimal)[]
        {
            (2m, true, new Dictionary<int, decimal> { [1] = 5m, [2] = 3m }, 8m), (1m, false, new Dictionary<int, decimal>(), 10m),
        }));
        Assert.Equal(new[] { (0, 2m), (1, 1m) }, AssembledKitRules.SplitReturn(3m, new[] { (4m, 2m), (5m, 0m) }));
        Assert.Contains(AdjustmentReasons.KitAssembly, AdjustmentRules.SystemReasons);
        Assert.Contains(AdjustmentReasons.KitDisassembly, AdjustmentRules.SystemReasons);
        Assert.Contains(ProductTypes.AssembledKit, ProductTypes.All);
        Assert.True(Teikem.Domain.Orders.NumberingRules.IsKnownKind(AssembledKitCodes.AssemblyNumberKind));
        Assert.True(Teikem.Domain.Orders.NumberingRules.IsKnownKind(AssembledKitCodes.DisassemblyNumberKind));
    }
}
