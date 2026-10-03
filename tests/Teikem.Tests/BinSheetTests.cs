using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 23 — hojas de posición: regla pura del estado (BinSheetRules), detección del cambio del CONJUNTO de productos en
/// InventoryLedger (entra un producto, sale a cero, sumando lotes; transferencia en origen y destino; ajuste; conteo;
/// reconstrucción del saldo), listado de posiciones con sheetStatus/staleCount, GET de hojas (productos agregados por producto,
/// solo con existencia, por SKU; tope 200), mark-printed (404 de otro almacén u otro tenant, idempotente, nunca retrocede) y
/// traducción a SQL Server sin BD. InMemory con el ledger real (WmsFixture).
/// </summary>
public sealed class BinSheetTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    // ================================================================ regla pura

    [Theory]
    [InlineData(true, null, null, BinSheetStatuses.NeverPrinted)]
    [InlineData(true, null, 5, BinSheetStatuses.NeverPrinted)]
    [InlineData(true, 10, null, BinSheetStatuses.Current)]
    [InlineData(true, 10, 5, BinSheetStatuses.Current)]
    [InlineData(true, 10, 10, BinSheetStatuses.Current)]
    [InlineData(true, 10, 11, BinSheetStatuses.Stale)]
    [InlineData(false, null, null, BinSheetStatuses.Empty)]
    [InlineData(false, null, 5, BinSheetStatuses.Empty)]
    [InlineData(false, 10, 5, BinSheetStatuses.Empty)]
    [InlineData(false, 10, 11, BinSheetStatuses.Stale)]   // impresa y quedó vacía después: la hoja pegada ya no sirve
    public void Sheet_status_follows_products_print_and_change(bool hasProducts, int? printedMin, int? changedMin, string expected)
    {
        DateTime? At(int? m) => m is int v ? T0.AddMinutes(v) : null;
        Assert.Equal(expected, BinSheetRules.Status(hasProducts, At(printedMin), At(changedMin)));
        Assert.Equal(expected is BinSheetStatuses.Stale or BinSheetStatuses.NeverPrinted, BinSheetRules.NeedsPrinting(expected));
    }

    [Theory]
    [InlineData(0, 3, true)]
    [InlineData(3, 0, true)]
    [InlineData(3, 7, false)]
    [InlineData(7, 3, false)]
    [InlineData(0, 0, false)]
    public void Content_changes_only_when_the_total_crosses_zero(double before, double after, bool expected)
        => Assert.Equal(expected, BinSheetRules.ContentChanged((decimal)before, (decimal)after));

    [Fact]
    public void Sheet_status_filter_is_parsed_and_unknown_is_rejected_with_the_exact_message()
    {
        Assert.Equal(((IReadOnlySet<string>?)null, (string?)null), BinSheetRules.ParseStatuses(null));
        var (codes, error) = BinSheetRules.ParseStatuses(new[] { " stale ", "never_printed,CURRENT", "" });
        Assert.Null(error);
        Assert.Equal(new[] { "CURRENT", "NEVER_PRINTED", "STALE" }, codes!.OrderBy(c => c, StringComparer.Ordinal));
        var (none, bad) = BinSheetRules.ParseStatuses(new[] { "STALE", "Vieja" });
        Assert.Null(none);
        Assert.Equal("Estado de hoja desconocido: 'Vieja'. Use NEVER_PRINTED, STALE, CURRENT o EMPTY.", bad);
    }

    [Fact]
    public void Printed_mark_uses_the_data_instant_never_the_future_and_never_goes_back()
    {
        var now = T0.AddHours(1);
        Assert.Equal(now, BinSheetRules.PrintedMark(now, null, null));
        Assert.Equal(T0, BinSheetRules.PrintedMark(now, T0, null));
        Assert.Equal(now, BinSheetRules.PrintedMark(now, now.AddDays(1), null));            // nunca en el futuro
        Assert.Equal(T0.AddMinutes(30), BinSheetRules.PrintedMark(now, T0, T0.AddMinutes(30)));   // no retrocede
        Assert.Equal(T0, BinSheetRules.PrintedMark(now, T0, T0));                            // idempotente
    }

    [Fact]
    public void Exact_messages_of_the_lot()
    {
        Assert.Equal("Se pueden pedir como máximo 200 hojas de posición por consulta; use skip para pedir las siguientes.", BinSheetRules.TakeTooLarge);
        Assert.Equal("Indique al menos una posición para marcar su hoja como impresa.", BinSheetRules.MarkPrintedEmpty);
        Assert.Equal("Se pueden marcar como máximo 500 posiciones por solicitud.", BinSheetRules.MarkPrintedTooMany);
    }

    // ================================================================ mundo

    private sealed record World(WmsFixture F, Warehouse W, WarehouseZone Z, WarehouseBin A, WarehouseBin B, WarehouseBin C, Product X, Product Y,
        Product L) : IAsyncDisposable
    {
        public WarehouseLayoutService Layout => F.Get<WarehouseLayoutService>();
        public BinSheetService Sheets => F.Get<BinSheetService>();
        public ValueTask DisposeAsync() => F.DisposeAsync();
    }

    /// <summary>Almacén W1, zona PCK con posiciones A-01, A-02 y A-03 vacías; productos X, Y (sin seguimiento) y L (por lote).</summary>
    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<WarehouseLayoutService>();
            s.AddSingleton<BinSheetService>();
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<InventoryReadService>();
            s.AddSingleton<InventoryAdjustmentService>();
        });
        var w = await f.AddWarehouseAsync("W1");
        var z = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var a = await f.AddBinAsync(z, "A-01", aisle: "A", rack: "01");
        var b = await f.AddBinAsync(z, "A-02", aisle: "A", rack: "02");
        var c = await f.AddBinAsync(z, "A-03", aisle: "B", rack: "03");
        var x = await f.AddProductAsync("SKU-X");
        var y = await f.AddProductAsync("SKU-Y");
        var l = await f.AddProductAsync("SKU-L", TrackingTypes.Lot);
        return new World(f, w, z, a, b, c, x, y, l);
    }

    private static async Task RunAsync(Func<World, Task> body)
    {
        await using var x = await WorldAsync();
        await body(x);
    }

    private static InventoryPosting In(World x, Product p, WarehouseBin b, decimal q, int? lotId = null)
        => new(InventoryTxnTypes.Receipt, p.ProductId, q, ToWarehouseId: x.W.WarehouseId, ToBinId: b.WarehouseBinId, LotId: lotId);

    private static InventoryPosting Out(World x, Product p, WarehouseBin b, decimal q, int? lotId = null)
        => new(InventoryTxnTypes.Issue, p.ProductId, q, FromWarehouseId: x.W.WarehouseId, FromBinId: b.WarehouseBinId, LotId: lotId);

    private static InventoryPosting Move(World x, Product p, WarehouseBin from, WarehouseBin to, decimal q)
        => new(InventoryTxnTypes.Transfer, p.ProductId, q, FromWarehouseId: x.W.WarehouseId, FromBinId: from.WarehouseBinId,
            ToWarehouseId: x.W.WarehouseId, ToBinId: to.WarehouseBinId);

    private static async Task<WarehouseBin> BinAsync(World x, WarehouseBin b)
        => await x.F.Db.WarehouseBins.AsNoTracking().SingleAsync(r => r.WarehouseBinId == b.WarehouseBinId);

    private static async Task<DateTime?> ChangedAsync(World x, WarehouseBin b) => (await BinAsync(x, b)).SheetContentChangedAtUtc;

    /// <summary>Fija la marca de cambio en un instante conocido (solo en la prueba: en src la escribe SOLO el ledger).</summary>
    private static async Task SetChangedAsync(World x, WarehouseBin b, DateTime? at)
    {
        var bin = await x.F.Db.WarehouseBins.AsTracking().SingleAsync(r => r.WarehouseBinId == b.WarehouseBinId);
        bin.SheetContentChangedAtUtc = at;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    // ================================================================ detección en el ledger

    [Fact]
    public Task New_product_marks_more_quantity_does_not_and_reaching_zero_marks()
        => RunAsync(async x =>
        {
            Assert.Null(await ChangedAsync(x, x.A));
            var before = DateTime.UtcNow;
            await x.F.PostAsync(In(x, x.X, x.A, 5m));
            var first = await ChangedAsync(x, x.A);
            Assert.NotNull(first);
            Assert.True(first >= before);

            // Más cantidad del mismo producto: el conjunto no cambia.
            await SetChangedAsync(x, x.A, T0);
            await x.F.PostAsync(In(x, x.X, x.A, 3m));
            Assert.Equal(T0, await ChangedAsync(x, x.A));

            // Baja sin llegar a cero: tampoco.
            await x.F.PostAsync(Out(x, x.X, x.A, 7m));
            Assert.Equal(T0, await ChangedAsync(x, x.A));

            // Otro producto entra: cambia.
            await x.F.PostAsync(In(x, x.Y, x.A, 1m));
            Assert.True(await ChangedAsync(x, x.A) > T0);

            // Baja a cero: cambia.
            await SetChangedAsync(x, x.A, T0);
            await x.F.PostAsync(Out(x, x.X, x.A, 1m));
            Assert.True(await ChangedAsync(x, x.A) > T0);

            // Las demás posiciones no se tocan.
            Assert.Null(await ChangedAsync(x, x.B));
        });

    [Fact]
    public Task Lots_are_summed_one_lot_to_zero_does_not_change_the_set()
        => RunAsync(async x =>
        {
            var l1 = await x.F.AddLotAsync(x.L, "L1");
            var l2 = await x.F.AddLotAsync(x.L, "L2");
            await x.F.PostAsync(In(x, x.L, x.A, 2m, l1.LotId));
            await SetChangedAsync(x, x.A, T0);

            // Un segundo lote del mismo producto: el producto ya estaba.
            await x.F.PostAsync(In(x, x.L, x.A, 4m, l2.LotId));
            Assert.Equal(T0, await ChangedAsync(x, x.A));

            // Un lote a cero con el otro aún en existencia: el producto sigue.
            await x.F.PostAsync(Out(x, x.L, x.A, 2m, l1.LotId));
            Assert.Equal(T0, await ChangedAsync(x, x.A));

            // El último lote a cero: el producto sale.
            await x.F.PostAsync(Out(x, x.L, x.A, 4m, l2.LotId));
            Assert.True(await ChangedAsync(x, x.A) > T0);

            // Dos lotes que entran juntos en un mismo asiento a una posición vacía: cambia una vez.
            await x.F.PostAsync(In(x, x.L, x.B, 1m, l1.LotId), In(x, x.L, x.B, 1m, l2.LotId));
            Assert.NotNull(await ChangedAsync(x, x.B));
        });

    [Fact]
    public Task Transfer_marks_origin_and_destination_when_each_set_changes()
        => RunAsync(async x =>
        {
            await x.F.PostAsync(In(x, x.X, x.A, 5m), In(x, x.X, x.C, 1m));
            await SetChangedAsync(x, x.A, T0);
            await SetChangedAsync(x, x.C, T0);

            // Parcial: el origen conserva el producto (sin cambio); el destino lo recibe por primera vez (cambio).
            await x.F.PostAsync(Move(x, x.X, x.A, x.B, 2m));
            Assert.Equal(T0, await ChangedAsync(x, x.A));
            Assert.NotNull(await ChangedAsync(x, x.B));

            // Todo lo que queda: el origen se vacía (cambio); el destino ya lo tenía (sin cambio).
            await SetChangedAsync(x, x.B, T0);
            await x.F.PostAsync(Move(x, x.X, x.A, x.B, 3m));
            Assert.True(await ChangedAsync(x, x.A) > T0);
            Assert.Equal(T0, await ChangedAsync(x, x.B));

            // Origen y destino cambian a la vez: C se vacía y A recibe.
            await SetChangedAsync(x, x.A, T0);
            await x.F.PostAsync(Move(x, x.X, x.C, x.A, 1m));
            Assert.True(await ChangedAsync(x, x.A) > T0);
            Assert.True(await ChangedAsync(x, x.C) > T0);
        });

    [Fact]
    public Task Adjustment_service_and_count_variance_go_through_the_same_detection()
        => RunAsync(async x =>
        {
            var adjust = x.F.Get<InventoryAdjustmentService>();
            await adjust.AdjustAsync(new AdjustmentRequest(x.X.PublicId, x.W.PublicId, x.A.WarehouseBinId, 2m, "FOUND", "prueba"), default);
            x.F.Db.ChangeTracker.Clear();
            Assert.NotNull(await ChangedAsync(x, x.A));

            await SetChangedAsync(x, x.A, T0);
            await adjust.AdjustAsync(new AdjustmentRequest(x.X.PublicId, x.W.PublicId, x.A.WarehouseBinId, -1m, "DAMAGE", "prueba"), default);
            x.F.Db.ChangeTracker.Clear();
            Assert.Equal(T0, await ChangedAsync(x, x.A));

            // Diferencia de conteo (el asiento que arma la reconciliación del conteo): faltante que deja la posición en cero.
            await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment, x.X.ProductId, 1m, FromWarehouseId: x.W.WarehouseId,
                FromBinId: x.A.WarehouseBinId, ReasonCode: AdjustmentReasons.CountVariance));
            Assert.True(await ChangedAsync(x, x.A) > T0);

            // Sobrante de conteo en una posición vacía: entra el producto.
            await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment, x.Y.ProductId, 4m, ToWarehouseId: x.W.WarehouseId,
                ToBinId: x.C.WarehouseBinId, ReasonCode: AdjustmentReasons.CountVariance));
            Assert.NotNull(await ChangedAsync(x, x.C));
        });

    [Fact]
    public Task Many_lines_into_many_bins_mark_each_bin_once_in_the_same_posting()
        => RunAsync(async x =>
        {
            await x.F.PostAsync(In(x, x.X, x.A, 1m), In(x, x.Y, x.A, 1m), In(x, x.X, x.B, 1m), In(x, x.X, x.B, 2m));
            var a = await ChangedAsync(x, x.A);
            Assert.NotNull(a);
            Assert.Equal(a, await ChangedAsync(x, x.B));   // un solo instante por asiento
            Assert.Null(await ChangedAsync(x, x.C));
        });

    [Fact]
    public Task A_failed_posting_does_not_mark_anything()
        => RunAsync(async x =>
        {
            await Assert.ThrowsAsync<InsufficientStockException>(() => x.F.PostAsync(In(x, x.X, x.B, 1m), Out(x, x.Y, x.A, 1m)));
            Assert.Null(await ChangedAsync(x, x.B));
        });

    [Fact]
    public Task Rebuilding_the_balance_from_the_kardex_also_marks_when_the_set_changes()
        => RunAsync(async x =>
        {
            await x.F.PostAsync(In(x, x.X, x.A, 3m));
            await SetChangedAsync(x, x.A, T0);
            // Descuadre: el saldo dice 0 aunque el Kárdex dice 3 (lo provoca la prueba directamente en el saldo).
            var balance = await x.F.Db.StockBalances.AsTracking().SingleAsync(s => s.WarehouseBinId == x.A.WarehouseBinId);
            balance.QtyOnHand = 0m;
            await x.F.Db.SaveChangesAsync();
            x.F.Db.ChangeTracker.Clear();

            var key = new BalanceKey(x.X.ProductId, x.W.WarehouseId, x.A.WarehouseBinId, null);
            await x.F.Db.RunInTransactionAsync(ct => x.F.Ledger.RebuildBalanceAsync(key, ct), default);
            x.F.Db.ChangeTracker.Clear();
            Assert.True(await ChangedAsync(x, x.A) > T0);
        });

    // ================================================================ listado de posiciones

    /// <summary>
    /// A-01 con X y nunca impresa (NEVER_PRINTED); A-02 con X, impresa después del último cambio (CURRENT); A-03 impresa y con
    /// cambio posterior, vacía (STALE); A-04 vacía sin impresión (EMPTY); A-05 con Y, impresa y cambiada después (STALE).
    /// </summary>
    private static async Task<(WarehouseBin D, WarehouseBin E)> StatesAsync(World x)
    {
        var d = await x.F.AddBinAsync(x.Z, "A-04");
        var e = await x.F.AddBinAsync(x.Z, "A-05");
        await x.F.PostAsync(In(x, x.X, x.A, 1m), In(x, x.X, x.B, 1m), In(x, x.Y, e, 2m));
        await x.Sheets.MarkPrintedAsync(x.W.PublicId, new BinSheetMarkPrintedRequest(new[] { x.B.WarehouseBinId, x.C.WarehouseBinId, e.WarehouseBinId }), default);
        x.F.Db.ChangeTracker.Clear();
        var printed = (await BinAsync(x, x.B)).SheetPrintedAtUtc!.Value;
        await SetChangedAsync(x, x.B, printed.AddMinutes(-1));
        await SetChangedAsync(x, x.C, printed.AddMinutes(1));
        await SetChangedAsync(x, e, printed.AddMinutes(1));
        return (d, e);
    }

    [Fact]
    public Task Bin_list_carries_sheet_status_and_stale_count_and_filters_by_it()
        => RunAsync(async x =>
        {
            await StatesAsync(x);
            var page = await x.Layout.ListBinsAsync(x.W.PublicId, new WarehouseBinQuery(), default);
            var byCode = page.Items.ToDictionary(b => b.Code, b => b.SheetStatus);
            Assert.Equal(new Dictionary<string, string>
            {
                ["A-01"] = BinSheetStatuses.NeverPrinted, ["A-02"] = BinSheetStatuses.Current, ["A-03"] = BinSheetStatuses.Stale,
                ["A-04"] = BinSheetStatuses.Empty, ["A-05"] = BinSheetStatuses.Stale,
            }, byCode);
            Assert.Equal(3, page.StaleCount);
            var a02 = page.Items.Single(b => b.Code == "A-02");
            Assert.NotNull(a02.SheetPrintedAtUtc);
            Assert.True(a02.SheetContentChangedAtUtc < a02.SheetPrintedAtUtc);

            // El filtro en SQL y BinSheetRules.Status (el del DTO) dicen lo mismo, estado por estado.
            foreach (var status in BinSheetStatuses.All)
            {
                var filtered = await x.Layout.ListBinsAsync(x.W.PublicId, new WarehouseBinQuery(SheetStatus: new[] { status.ToLowerInvariant() }), default);
                Assert.Equal(byCode.Where(kv => kv.Value == status).Select(kv => kv.Key).OrderBy(c => c), filtered.Items.Select(b => b.Code));
                Assert.Equal(BinSheetRules.NeedsPrinting(status) ? filtered.Total : 0, filtered.StaleCount);
            }

            // Varios estados; el staleCount es del filtro actual (todas las páginas, no solo la visible).
            var several = await x.Layout.ListBinsAsync(x.W.PublicId, new WarehouseBinQuery(SheetStatus: new[] { "STALE,NEVER_PRINTED" }, Take: 1), default);
            Assert.Equal((3, 3, 1), (several.Total, several.StaleCount, several.Items.Count));
            var zoneB = await x.Layout.ListBinsAsync(x.W.PublicId, new WarehouseBinQuery(Aisle: "B"), default);
            Assert.Equal((1, 1), (zoneB.Total, zoneB.StaleCount));

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                x.Layout.ListBinsAsync(x.W.PublicId, new WarehouseBinQuery(SheetStatus: new[] { "OLD" }), default));
            Assert.Equal(BinSheetRules.UnknownSheetStatus("OLD"), Assert.Single(ex.Errors!["sheetStatus"]));
        });

    [Fact]
    public Task Activating_the_feature_leaves_every_bin_with_product_never_printed()
        => RunAsync(async x =>
        {
            await x.F.PostAsync(In(x, x.X, x.A, 1m), In(x, x.Y, x.B, 1m));
            var page = await x.Layout.ListBinsAsync(x.W.PublicId, new WarehouseBinQuery(OnlyWithStock: true), default);
            Assert.All(page.Items, b => Assert.Equal(BinSheetStatuses.NeverPrinted, b.SheetStatus));
            Assert.All(page.Items, b => Assert.Null(b.SheetPrintedAtUtc));
            Assert.Equal(2, page.StaleCount);
        });

    // ================================================================ hojas

    [Fact]
    public Task Sheets_aggregate_products_without_repeating_lots_only_on_hand_ordered_by_sku()
        => RunAsync(async x =>
        {
            var l1 = await x.F.AddLotAsync(x.L, "L1");
            var l2 = await x.F.AddLotAsync(x.L, "L2");
            var z = await x.F.AddProductAsync("SKU-Z");
            await x.F.PostAsync(In(x, z, x.A, 1m), In(x, x.L, x.A, 2m, l1.LotId), In(x, x.L, x.A, 3m, l2.LotId), In(x, x.X, x.A, 1m),
                In(x, x.Y, x.A, 1m));
            await x.F.PostAsync(Out(x, x.Y, x.A, 1m));   // Y salió: no va en la hoja
            var bc = await x.F.Db.Products.AsTracking().SingleAsync(p => p.ProductId == x.X.ProductId);
            bc.Barcode = "7501234567890";
            await x.F.Db.SaveChangesAsync();
            x.F.Db.ChangeTracker.Clear();

            var page = await x.Sheets.ListAsync(x.W.PublicId, new BinSheetQuery(new WarehouseBinQuery(BinIds: new[] { x.A.WarehouseBinId })), default);
            var sheet = Assert.Single(page.Items);
            Assert.Equal(("A-01", "PCK", "A", "01", BinSheetStatuses.NeverPrinted), (sheet.Code, sheet.ZoneCode, sheet.Aisle, sheet.Rack, sheet.SheetStatus));
            Assert.Equal(new[] { "SKU-L", "SKU-X", "SKU-Z" }, sheet.Products.Select(p => p.Sku));
            var px = sheet.Products.Single(p => p.Sku == "SKU-X");
            Assert.Equal((x.X.PublicId, "Producto SKU-X", "7501234567890"), (px.ProductPublicId, px.Name, px.Barcode));
            Assert.Equal((1, 1), (page.Total, page.StaleCount));
            Assert.True(page.GeneratedAtUtc <= DateTime.UtcNow);
        });

    [Fact]
    public Task Sheets_use_the_bin_list_filters_paginate_and_reject_more_than_200()
        => RunAsync(async x =>
        {
            await StatesAsync(x);
            var stale = await x.Sheets.ListAsync(x.W.PublicId, new BinSheetQuery(new WarehouseBinQuery(SheetStatus: new[] { BinSheetStatuses.Stale })), default);
            Assert.Equal(new[] { "A-03", "A-05" }, stale.Items.Select(s => s.Code));
            Assert.Empty(stale.Items[0].Products);   // A-03 quedó vacía: su hoja nueva no lleva productos
            Assert.Equal("SKU-Y", Assert.Single(stale.Items[1].Products).Sku);

            var paged = await x.Sheets.ListAsync(x.W.PublicId, new BinSheetQuery(new WarehouseBinQuery(), Skip: 1, Take: 2), default);
            Assert.Equal((5, 1, 2), (paged.Total, paged.Skip, paged.Take));
            Assert.Equal(new[] { "A-02", "A-03" }, paged.Items.Select(s => s.Code));
            Assert.Equal(50, (await x.Sheets.ListAsync(x.W.PublicId, new BinSheetQuery(new WarehouseBinQuery()), default)).Take);

            var aisle = await x.Sheets.ListAsync(x.W.PublicId, new BinSheetQuery(new WarehouseBinQuery(Aisle: "b")), default);
            Assert.Equal(new[] { "A-03" }, aisle.Items.Select(s => s.Code));

            var tooMany = await Assert.ThrowsAsync<ValidationException>(() =>
                x.Sheets.ListAsync(x.W.PublicId, new BinSheetQuery(new WarehouseBinQuery(), Take: 201), default));
            Assert.Equal(BinSheetRules.TakeTooLarge, Assert.Single(tooMany.Errors!["take"]));
            Assert.Equal(200, (await x.Sheets.ListAsync(x.W.PublicId, new BinSheetQuery(new WarehouseBinQuery(), Take: 200), default)).Take);

            var unknown = await Assert.ThrowsAsync<ValidationException>(() =>
                x.Sheets.ListAsync(x.W.PublicId, new BinSheetQuery(new WarehouseBinQuery(SheetStatus: new[] { "X" })), default));
            Assert.Equal(BinSheetRules.UnknownSheetStatus("X"), Assert.Single(unknown.Errors!["sheetStatus"]));
        });

    // ================================================================ marcar impresas

    [Fact]
    public Task Mark_printed_sets_the_mark_returns_the_new_state_and_is_idempotent()
        => RunAsync(async x =>
        {
            await x.F.PostAsync(In(x, x.X, x.A, 1m));
            var generated = DateTime.UtcNow;   // el instante de los datos de la hoja: después del último cambio
            var result = await x.Sheets.MarkPrintedAsync(x.W.PublicId,
                new BinSheetMarkPrintedRequest(new[] { x.B.WarehouseBinId, x.A.WarehouseBinId, x.A.WarehouseBinId }, generated), default);
            Assert.Equal(new[] { "A-01", "A-02" }, result.Select(r => r.Code));
            Assert.Equal(new[] { BinSheetStatuses.Current, BinSheetStatuses.Empty }, result.Select(r => r.SheetStatus));
            Assert.All(result, r => Assert.Equal(generated, r.SheetPrintedAtUtc));
            x.F.Db.ChangeTracker.Clear();
            Assert.Equal(generated, (await BinAsync(x, x.A)).SheetPrintedAtUtc);

            // Repetir la misma marca no cambia nada.
            var again = await x.Sheets.MarkPrintedAsync(x.W.PublicId, new BinSheetMarkPrintedRequest(new[] { x.A.WarehouseBinId }, generated), default);
            Assert.Equal(generated, Assert.Single(again).SheetPrintedAtUtc);
            x.F.Db.ChangeTracker.Clear();

            // Un cambio del conjunto después de la impresión → STALE; volver a imprimir (sin instante = ahora) → CURRENT.
            await x.F.PostAsync(In(x, x.Y, x.A, 1m));
            Assert.Equal(BinSheetStatuses.Stale, (await x.Layout.ListBinsAsync(x.W.PublicId, new WarehouseBinQuery(BinIds: new[] { x.A.WarehouseBinId }), default)).Items[0].SheetStatus);
            var reprinted = await x.Sheets.MarkPrintedAsync(x.W.PublicId, new BinSheetMarkPrintedRequest(new[] { x.A.WarehouseBinId }), default);
            Assert.Equal(BinSheetStatuses.Current, Assert.Single(reprinted).SheetStatus);
            x.F.Db.ChangeTracker.Clear();
        });

    [Fact]
    public Task A_change_between_reading_the_sheet_and_marking_it_leaves_it_stale()
        => RunAsync(async x =>
        {
            await x.F.PostAsync(In(x, x.X, x.A, 1m));
            var sheets = await x.Sheets.ListAsync(x.W.PublicId, new BinSheetQuery(new WarehouseBinQuery(BinIds: new[] { x.A.WarehouseBinId })), default);
            await Task.Delay(5);
            await x.F.PostAsync(In(x, x.Y, x.A, 1m));   // entra un producto mientras se imprime
            var marked = await x.Sheets.MarkPrintedAsync(x.W.PublicId, new BinSheetMarkPrintedRequest(new[] { x.A.WarehouseBinId }, sheets.GeneratedAtUtc), default);
            Assert.Equal(BinSheetStatuses.Stale, Assert.Single(marked).SheetStatus);
        });

    [Fact]
    public Task Mark_printed_validates_the_list_and_rejects_bins_of_another_warehouse_without_writing()
        => RunAsync(async x =>
        {
            var empty = await Assert.ThrowsAsync<ValidationException>(() =>
                x.Sheets.MarkPrintedAsync(x.W.PublicId, new BinSheetMarkPrintedRequest(Array.Empty<int>()), default));
            Assert.Equal(BinSheetRules.MarkPrintedEmpty, Assert.Single(empty.Errors!["binIds"]));
            await Assert.ThrowsAsync<ValidationException>(() => x.Sheets.MarkPrintedAsync(x.W.PublicId, new BinSheetMarkPrintedRequest(null), default));
            var many = await Assert.ThrowsAsync<ValidationException>(() =>
                x.Sheets.MarkPrintedAsync(x.W.PublicId, new BinSheetMarkPrintedRequest(Enumerable.Range(1, 501).ToArray()), default));
            Assert.Equal(BinSheetRules.MarkPrintedTooMany, Assert.Single(many.Errors!["binIds"]));

            var w2 = await x.F.AddWarehouseAsync("W2");
            var z2 = await x.F.AddZoneAsync(w2, "PCK", ZoneTypes.Picking);
            var other = await x.F.AddBinAsync(z2, "Z-01");
            var nf = await Assert.ThrowsAsync<NotFoundException>(() => x.Sheets.MarkPrintedAsync(x.W.PublicId,
                new BinSheetMarkPrintedRequest(new[] { x.A.WarehouseBinId, other.WarehouseBinId }), default));
            Assert.Equal("Posición no encontrada.", nf.Message);
            x.F.Db.ChangeTracker.Clear();
            Assert.Null((await BinAsync(x, x.A)).SheetPrintedAtUtc);   // todo o nada
            await Assert.ThrowsAsync<NotFoundException>(() => x.Sheets.MarkPrintedAsync(x.W.PublicId, new BinSheetMarkPrintedRequest(new[] { 999_999 }), default));
        });

    [Fact]
    public Task Another_tenant_cannot_read_or_mark_the_sheets()
        => RunAsync(async x =>
        {
            await x.F.PostAsync(In(x, x.X, x.A, 1m));
            var foreign = await x.F.AddWarehouseAsync("W9", WmsFixture.OtherTenantId);
            using (x.F.AsTenant(WmsFixture.OtherTenantId))
            {
                await Assert.ThrowsAsync<NotFoundException>(() => x.Sheets.ListAsync(x.W.PublicId, new BinSheetQuery(new WarehouseBinQuery()), default));
                await Assert.ThrowsAsync<NotFoundException>(() =>
                    x.Sheets.MarkPrintedAsync(x.W.PublicId, new BinSheetMarkPrintedRequest(new[] { x.A.WarehouseBinId }), default));
                // Ni nombrando la posición desde un almacén propio.
                await Assert.ThrowsAsync<NotFoundException>(() =>
                    x.Sheets.MarkPrintedAsync(foreign.PublicId, new BinSheetMarkPrintedRequest(new[] { x.A.WarehouseBinId }), default));
            }
            x.F.Db.ChangeTracker.Clear();
            Assert.Null((await BinAsync(x, x.A)).SheetPrintedAtUtc);
        });

    // ================================================================ traducción a SQL Server (sin BD) y modelo

    [Fact]
    public void Sheet_status_filter_and_stale_count_translate_to_sql_server()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var statuses = BinSheetRules.ParseStatuses(new[] { "NEVER_PRINTED,STALE,CURRENT,EMPTY" }).Codes;
        var q = WarehouseLayoutService.BinRowsQuery(db, 1, new WarehouseBinQuery(), null, null, statuses);
        var sql = q.OrderBy(x => x.Bin.Code).Skip(0).Take(10).ToQueryString();
        Assert.Contains("[SheetPrintedAtUtc]", sql);
        Assert.Contains("[SheetContentChangedAtUtc]", sql);
        Assert.Contains("[SheetPrintedAtUtc]", q.Where(WarehouseLayoutService.NeedsSheetPrinting).ToQueryString());
        var pairs = BinSheetService.ProductPairsQuery(db, 1, new List<int> { 1, 2 }).ToQueryString();
        Assert.Contains("GROUP BY", pairs);
        Assert.Contains("HAVING", pairs);
    }

    [Fact]
    public void Sheet_columns_are_mapped_not_audited_and_declared_in_the_structure_script()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var entity = db.Model.FindEntityType(typeof(WarehouseBin))!;
        foreach (var name in new[] { nameof(WarehouseBin.SheetPrintedAtUtc), nameof(WarehouseBin.SheetContentChangedAtUtc) })
        {
            var p = entity.FindProperty(name)!;
            Assert.Equal("datetime2", p.GetColumnType());
            Assert.True(p.IsNullable);
            Assert.NotNull(typeof(WarehouseBin).GetProperty(name)!.GetCustomAttributes(typeof(NotAuditedAttribute), false).SingleOrDefault());
        }
        var sql = File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-estructura.sql"));
        Assert.Contains("SheetPrintedAtUtc DATETIME2 NULL,", sql);
        Assert.Contains("SheetContentChangedAtUtc DATETIME2 NULL,", sql);
        Assert.Contains("IF COL_LENGTH('dbo.WarehouseBin', 'SheetPrintedAtUtc') IS NULL", sql);
        Assert.Contains("IF COL_LENGTH('dbo.WarehouseBin', 'SheetContentChangedAtUtc') IS NULL", sql);
    }
}
