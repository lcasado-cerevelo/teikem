using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 1 de cambios de Almacén — cupo de posición (MaxCapacityQty), estado de ocupación, listado de posiciones paginado en el
/// servidor (búsqueda por pasillo/rack/nivel/posición, filtros nuevos), ocupación por zona, código de zona editable y tipos de
/// zona en la lista de almacenes. Reglas puras + servicio (InMemory, WmsFixture) + traducción a SQL Server sin BD (ToQueryString).
/// </summary>
public sealed class WarehouseOccupancyTests
{
    // ================================================================ reglas puras

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(5000)]
    public void Max_capacity_null_or_positive_is_valid(int? qty) => Assert.Null(WarehouseRules.ValidateMaxCapacity(qty));

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Max_capacity_zero_or_negative_is_rejected(int qty)
        => Assert.Equal("El cupo máximo de la posición debe ser mayor que cero.", WarehouseRules.ValidateMaxCapacity(qty));

    [Theory]
    [InlineData(0, null, BinOccupancies.Empty)]
    [InlineData(0, 10, BinOccupancies.Empty)]
    [InlineData(3, null, BinOccupancies.NoCapacity)]
    [InlineData(4, 10, BinOccupancies.Partial)]
    [InlineData(9.999, 10, BinOccupancies.Partial)]
    [InlineData(10, 10, BinOccupancies.Full)]
    [InlineData(12, 10, BinOccupancies.Full)]
    public void Occupancy_compares_on_hand_with_capacity(double onHand, int? capacity, string expected)
        => Assert.Equal(expected, WarehouseRules.Occupancy((decimal)onHand, capacity));

    [Fact]
    public void Occupancy_filter_is_parsed_case_insensitive_and_comma_separated()
    {
        Assert.Equal(((IReadOnlySet<string>?)null, (string?)null), WarehouseRules.ParseOccupancy(null));
        Assert.Equal(((IReadOnlySet<string>?)null, (string?)null), WarehouseRules.ParseOccupancy(new[] { "", " " }));
        var (codes, error) = WarehouseRules.ParseOccupancy(new[] { " empty ", "full,partial", "FULL" });
        Assert.Null(error);
        Assert.Equal(new[] { BinOccupancies.Empty, BinOccupancies.Full, BinOccupancies.Partial }.OrderBy(c => c), codes!.OrderBy(c => c));
    }

    [Fact]
    public void Unknown_occupancy_is_rejected_with_the_exact_message()
    {
        var (codes, error) = WarehouseRules.ParseOccupancy(new[] { "EMPTY", "Lleno" });
        Assert.Null(codes);
        Assert.Equal("Estado de ocupación desconocido: 'Lleno'. Use EMPTY, PARTIAL, FULL o NO_CAPACITY.", error);
    }

    [Theory]
    [InlineData(-5, 0, 0, 100)]
    [InlineData(10, 25, 10, 25)]
    [InlineData(0, 1000, 0, 200)]
    public void Bin_page_is_bounded(int skip, int take, int expectedSkip, int expectedTake)
        => Assert.Equal((expectedSkip, expectedTake), WarehouseRules.BinPage(skip, take));

    // ================================================================ mundo de servicio

    private sealed record World(WmsFixture F, Warehouse W, WarehouseZone Pck, WarehouseZone Rsv, WarehouseBin P01, WarehouseBin P02,
        WarehouseBin P03, WarehouseBin R01, WarehouseBin R02, Product X, Product Y) : IAsyncDisposable
    {
        public WarehouseLayoutService Layout => F.Get<WarehouseLayoutService>();
        public ValueTask DisposeAsync() => F.DisposeAsync();
    }

    /// <summary>
    /// PCK: P-01 cupo 10 con 4 de X (PARTIAL, partes A07/RK3/N4/PZ9), P-02 cupo 5 con 5 de X (FULL), P-03 sin cupo con 1 de X
    /// y 2 de Y (NO_CAPACITY, 2 productos). RSV: R-01 cupo 20 vacía (EMPTY), R-02 inactiva.
    /// </summary>
    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s => { s.AddSingleton<WarehouseLayoutService>(); s.AddSingleton<WarehouseService>(); });
        var w = await f.AddWarehouseAsync("W1");
        var pck = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var rsv = await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve);
        var p01 = await f.AddBinAsync(pck, "P-01", maxCapacityQty: 10, aisle: "A07", rack: "RK3", level: "N4", position: "PZ9");
        var p02 = await f.AddBinAsync(pck, "P-02", maxCapacityQty: 5);
        var p03 = await f.AddBinAsync(pck, "P-03");
        var r01 = await f.AddBinAsync(rsv, "R-01", maxCapacityQty: 20);
        var r02 = await f.AddBinAsync(rsv, "R-02", isActive: false);
        var x = await f.AddProductAsync("SKU-X");
        var y = await f.AddProductAsync("SKU-Y");
        InventoryPosting In(Product p, WarehouseBin b, decimal q) => new(InventoryTxnTypes.Receipt, p.ProductId, q, ToWarehouseId: w.WarehouseId, ToBinId: b.WarehouseBinId);
        await f.PostAsync(In(x, p01, 4m), In(x, p02, 5m), In(x, p03, 1m), In(y, p03, 2m));
        f.Db.ChangeTracker.Clear();
        return new World(f, w, pck, rsv, p01, p02, p03, r01, r02, x, y);
    }

    private static async Task<string[]> CodesAsync(World x, WarehouseBinQuery q)
        => (await x.Layout.ListBinsAsync(x.W.PublicId, q, default)).Items.Select(b => b.Code).ToArray();

    // ================================================================ listado de posiciones

    [Theory]
    [InlineData("a07")]
    [InlineData("RK3")]
    [InlineData("n4")]
    [InlineData("pz9")]
    [InlineData("P-01")]
    public Task Search_matches_aisle_rack_level_and_position_not_only_the_code(string term)
        => RunAsync(async x => Assert.Equal(new[] { "P-01" }, await CodesAsync(x, new WarehouseBinQuery(Search: term))));

    [Fact]
    public Task Search_by_zone_code_still_works()
        => RunAsync(async x => Assert.Equal(new[] { "R-01" }, await CodesAsync(x, new WarehouseBinQuery(Search: "rsv"))));

    [Fact]
    public Task Bins_are_paginated_in_the_server_ordered_by_code()
        => RunAsync(async x =>
        {
            var page = await x.Layout.ListBinsAsync(x.W.PublicId, new WarehouseBinQuery(Skip: 1, Take: 2), default);
            Assert.Equal((4, 1, 2), (page.Total, page.Skip, page.Take));
            Assert.Equal(new[] { "P-02", "P-03" }, page.Items.Select(b => b.Code));

            var all = await x.Layout.ListBinsAsync(x.W.PublicId, new WarehouseBinQuery(IncludeInactive: true), default);
            Assert.Equal((5, 100), (all.Total, all.Take));
        });

    [Fact]
    public Task Bin_dto_carries_capacity_occupancy_stock_and_the_single_product()
        => RunAsync(async x =>
        {
            var items = (await x.Layout.ListBinsAsync(x.W.PublicId, new WarehouseBinQuery(), default)).Items.ToDictionary(b => b.Code);
            var p01 = items["P-01"];
            Assert.Equal(((int?)10, 4m, 1, BinOccupancies.Partial), (p01.MaxCapacityQty, p01.QtyOnHand, p01.ProductCount, p01.Occupancy));
            Assert.Equal(((Guid?)x.X.PublicId, "SKU-X", "Producto SKU-X"), (p01.SingleProductPublicId, p01.SingleProductSku, p01.SingleProductName));
            Assert.Equal(BinOccupancies.Full, items["P-02"].Occupancy);

            var p03 = items["P-03"];
            Assert.Equal(((int?)null, 3m, 2, BinOccupancies.NoCapacity), (p03.MaxCapacityQty, p03.QtyOnHand, p03.ProductCount, p03.Occupancy));
            Assert.Null(p03.SingleProductSku);

            var r01 = items["R-01"];
            Assert.Equal((0m, 0, BinOccupancies.Empty), (r01.QtyOnHand, r01.ProductCount, r01.Occupancy));
            Assert.Null(r01.SingleProductPublicId);
        });

    [Theory]
    [InlineData(BinOccupancies.Empty, new[] { "R-01" })]
    [InlineData(BinOccupancies.Partial, new[] { "P-01" })]
    [InlineData(BinOccupancies.Full, new[] { "P-02" })]
    [InlineData(BinOccupancies.NoCapacity, new[] { "P-03" })]
    public Task Occupancy_filter_matches_the_rule_of_each_row(string occupancy, string[] expected)
        => RunAsync(async x =>
        {
            var page = await x.Layout.ListBinsAsync(x.W.PublicId, new WarehouseBinQuery(Occupancy: new[] { occupancy.ToLowerInvariant() }), default);
            Assert.Equal(expected, page.Items.Select(b => b.Code));
            // El filtro en SQL y WarehouseRules.Occupancy (el del DTO) dicen lo mismo.
            Assert.All(page.Items, b => Assert.Equal(occupancy, b.Occupancy));
        });

    [Fact]
    public Task Several_occupancy_states_are_combined_and_an_unknown_one_is_400()
        => RunAsync(async x =>
        {
            Assert.Equal(new[] { "P-02", "R-01" }, await CodesAsync(x, new WarehouseBinQuery(Occupancy: new[] { "FULL", "EMPTY" })));
            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                x.Layout.ListBinsAsync(x.W.PublicId, new WarehouseBinQuery(Occupancy: new[] { "HALF" }), default));
            Assert.Equal(WarehouseRules.UnknownOccupancy("HALF"), Assert.Single(ex.Errors!["occupancy"]));
        });

    [Fact]
    public Task Zone_part_product_and_stock_filters_apply_in_the_query()
        => RunAsync(async x =>
        {
            Assert.Equal(new[] { "R-01", "R-02" }, await CodesAsync(x, new WarehouseBinQuery(IncludeInactive: true, ZoneIds: new[] { x.Rsv.WarehouseZoneId })));
            Assert.Equal(new[] { "P-01" }, await CodesAsync(x, new WarehouseBinQuery(Aisle: "a0")));
            Assert.Equal(new[] { "P-01" }, await CodesAsync(x, new WarehouseBinQuery(Rack: "RK")));
            Assert.Equal(new[] { "P-01" }, await CodesAsync(x, new WarehouseBinQuery(Level: "N4")));
            Assert.Equal(new[] { "P-01" }, await CodesAsync(x, new WarehouseBinQuery(Position: "Z9")));
            Assert.Equal(new[] { "P-03" }, await CodesAsync(x, new WarehouseBinQuery(ProductPublicIds: new[] { x.Y.PublicId })));
            Assert.Equal(new[] { "P-01", "P-02", "P-03" }, await CodesAsync(x, new WarehouseBinQuery(ProductPublicIds: new[] { x.X.PublicId })));
            Assert.Empty(await CodesAsync(x, new WarehouseBinQuery(ProductPublicIds: new[] { Guid.NewGuid() })));
            Assert.Equal(new[] { "P-01", "P-02", "P-03" }, await CodesAsync(x, new WarehouseBinQuery(OnlyWithStock: true)));
            Assert.Equal(new[] { "P-02", "R-02" }, await CodesAsync(x, new WarehouseBinQuery(IncludeInactive: true,
                BinIds: new[] { x.P02.WarehouseBinId, x.R02.WarehouseBinId })));
        });

    [Fact]
    public Task Zone_of_another_warehouse_is_404()
        => RunAsync(async x =>
        {
            var w2 = await x.F.AddWarehouseAsync("W2");
            var z2 = await x.F.AddZoneAsync(w2, "PCK", ZoneTypes.Picking);
            await Assert.ThrowsAsync<NotFoundException>(() => x.Layout.ListBinsAsync(x.W.PublicId, new WarehouseBinQuery(ZoneId: z2.WarehouseZoneId), default));
        });

    // ================================================================ cupo de la posición

    [Fact]
    public Task Bin_capacity_is_validated_created_patched_and_cleared()
        => RunAsync(async x =>
        {
            var bad = await Assert.ThrowsAsync<ValidationException>(() =>
                x.Layout.CreateBinAsync(x.W.PublicId, new WarehouseBinRequest(x.Rsv.WarehouseZoneId, "R-09", MaxCapacityQty: 0), default));
            Assert.Equal(WarehouseRules.MaxCapacityQtyMessage, Assert.Single(bad.Errors!["maxCapacityQty"]));

            var created = await x.Layout.CreateBinAsync(x.W.PublicId, new WarehouseBinRequest(x.Rsv.WarehouseZoneId, "R-09", MaxCapacityQty: 12), default);
            Assert.Equal(((int?)12, BinOccupancies.Empty), (created.MaxCapacityQty, created.Occupancy));
            x.F.Db.ChangeTracker.Clear();

            var badPatch = await Assert.ThrowsAsync<ValidationException>(() =>
                x.Layout.UpdateBinAsync(x.W.PublicId, created.Id, new WarehouseBinPatchRequest(MaxCapacityQty: -1), default));
            Assert.Equal(WarehouseRules.MaxCapacityQtyMessage, Assert.Single(badPatch.Errors!["maxCapacityQty"]));

            var patched = await x.Layout.UpdateBinAsync(x.W.PublicId, x.P01.WarehouseBinId, new WarehouseBinPatchRequest(MaxCapacityQty: 4), default);
            Assert.Equal(((int?)4, BinOccupancies.Full, "SKU-X"), (patched.MaxCapacityQty, patched.Occupancy, patched.SingleProductSku));
            x.F.Db.ChangeTracker.Clear();
            Assert.Equal(4, (await x.F.Db.WarehouseBins.AsNoTracking().SingleAsync(b => b.WarehouseBinId == x.P01.WarehouseBinId)).MaxCapacityQty);

            var cleared = await x.Layout.UpdateBinAsync(x.W.PublicId, x.P01.WarehouseBinId, new WarehouseBinPatchRequest(ClearMaxCapacity: true), default);
            Assert.Equal(((int?)null, BinOccupancies.NoCapacity), (cleared.MaxCapacityQty, cleared.Occupancy));
            x.F.Db.ChangeTracker.Clear();
            Assert.Null((await x.F.Db.WarehouseBins.AsNoTracking().SingleAsync(b => b.WarehouseBinId == x.P01.WarehouseBinId)).MaxCapacityQty);
        });

    // ================================================================ ocupación por zona

    [Fact]
    public Task Zone_list_brings_occupancy_computed_from_its_bins()
        => RunAsync(async x =>
        {
            var zones = (await x.Layout.ListZonesAsync(x.W.PublicId, false, default)).ToDictionary(z => z.Code);
            var pck = zones["PCK"];
            // Capacidad = 10 + 5 (P-03 sin cupo queda fuera y se cuenta aparte); en esas posiciones hay 4 + 5.
            Assert.Equal((3, 3, 15L, 9m, 12m, 1), (pck.BinCount, pck.OccupiedBinCount, pck.CapacityQty, pck.QtyOnHandInCapacityBins, pck.QtyOnHand, pck.BinsWithoutCapacity));
            var rsv = zones["RSV"];
            // R-02 inactiva: no cuenta como posición ni como "sin cupo".
            Assert.Equal((1, 0, 20L, 0m, 0m, 0), (rsv.BinCount, rsv.OccupiedBinCount, rsv.CapacityQty, rsv.QtyOnHandInCapacityBins, rsv.QtyOnHand, rsv.BinsWithoutCapacity));

            var detail = await x.F.Get<WarehouseService>().GetAsync(x.W.PublicId, default);
            Assert.Equal(pck, detail.Zones.Single(z => z.Code == "PCK"));
        });

    [Fact]
    public Task Zone_without_bins_has_zero_occupancy()
        => RunAsync(async x =>
        {
            var created = await x.Layout.CreateZoneAsync(x.W.PublicId, new WarehouseZoneRequest("STG", "Preparación", ZoneTypes.Staging), default);
            Assert.Equal((0, 0, 0L, 0m, 0m, 0), (created.BinCount, created.OccupiedBinCount, created.CapacityQty, created.QtyOnHandInCapacityBins, created.QtyOnHand, created.BinsWithoutCapacity));
        });

    // ================================================================ código de zona editable

    [Fact]
    public Task Zone_code_is_editable_required_and_unique_in_the_warehouse()
        => RunAsync(async x =>
        {
            var renamed = await x.Layout.UpdateZoneAsync(x.W.PublicId, x.Pck.WarehouseZoneId, new WarehouseZonePatchRequest(Code: " pck-2 "), default);
            Assert.Equal("PCK-2", renamed.Code);
            Assert.Equal(3, renamed.BinCount); // las posiciones siguen en la zona (apuntan por id)
            x.F.Db.ChangeTracker.Clear();
            Assert.Equal("PCK-2", (await x.F.Db.WarehouseZones.AsNoTracking().SingleAsync(z => z.WarehouseZoneId == x.Pck.WarehouseZoneId)).Code);

            var dup = await Assert.ThrowsAsync<ConflictException>(() =>
                x.Layout.UpdateZoneAsync(x.W.PublicId, x.Pck.WarehouseZoneId, new WarehouseZonePatchRequest(Code: "rsv"), default));
            Assert.Equal("Ya existe una zona con ese código en el almacén.", dup.Message);
            x.F.Db.ChangeTracker.Clear();

            var empty = await Assert.ThrowsAsync<ValidationException>(() =>
                x.Layout.UpdateZoneAsync(x.W.PublicId, x.Pck.WarehouseZoneId, new WarehouseZonePatchRequest(Code: "  "), default));
            Assert.Equal(WarehouseRules.CodeRequiredMessage, Assert.Single(empty.Errors!["code"]));

            var invalid = await Assert.ThrowsAsync<ValidationException>(() =>
                x.Layout.UpdateZoneAsync(x.W.PublicId, x.Pck.WarehouseZoneId, new WarehouseZonePatchRequest(Code: "PCK 2"), default));
            Assert.Equal(WarehouseRules.CodeInvalidMessage, Assert.Single(invalid.Errors!["code"]));

            // Mismo código que ya tiene: no es duplicado.
            Assert.Equal("PCK-2", (await x.Layout.UpdateZoneAsync(x.W.PublicId, x.Pck.WarehouseZoneId, new WarehouseZonePatchRequest(Code: "PCK-2"), default)).Code);

            // El mismo código en OTRO almacén sí se permite (la unicidad es por almacén).
            var w2 = await x.F.AddWarehouseAsync("W2");
            var z2 = await x.F.AddZoneAsync(w2, "OTRA", ZoneTypes.Picking);
            Assert.Equal("RSV", (await x.Layout.UpdateZoneAsync(w2.PublicId, z2.WarehouseZoneId, new WarehouseZonePatchRequest(Code: "RSV"), default)).Code);
        });

    [Fact]
    public Task Zone_cannot_move_to_another_warehouse()
        => RunAsync(async x =>
        {
            var req = new WarehouseZonePatchRequest(Name: "X")
            {
                Extra = new Dictionary<string, JsonElement> { ["warehouseId"] = JsonDocument.Parse("7").RootElement },
            };
            var ex = await Assert.ThrowsAsync<ValidationException>(() => x.Layout.UpdateZoneAsync(x.W.PublicId, x.Pck.WarehouseZoneId, req, default));
            Assert.Equal("La zona no se puede mover a otro almacén.", Assert.Single(ex.Errors!["warehouseId"]));
        });

    // ================================================================ tipos de zona en la lista de almacenes

    [Fact]
    public Task Warehouse_list_carries_the_distinct_zone_types_of_its_active_zones()
        => RunAsync(async x =>
        {
            await x.F.AddZoneAsync(x.W, "PCK-B", ZoneTypes.Picking);
            await x.F.AddZoneAsync(x.W, "CUA", ZoneTypes.Quarantine, isActive: false);
            var w2 = await x.F.AddWarehouseAsync("W2");
            x.F.Db.ChangeTracker.Clear();

            var list = (await x.F.Get<WarehouseService>().ListAsync(false, default)).ToDictionary(w => w.Code);
            Assert.Equal(new[] { ZoneTypes.Picking, ZoneTypes.Reserve }, list["W1"].ZoneTypeCodes);
            Assert.Empty(list["W2"].ZoneTypeCodes);
        });

    // ================================================================ traducción a SQL Server (sin BD)

    [Fact]
    public void Bin_list_query_with_every_filter_translates_to_sql_server()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var query = new WarehouseBinQuery(ZoneId: 3, Search: "a01", ZoneIds: new[] { 1, 2 }, Aisle: "A", Rack: "R", Level: "N", Position: "P",
            OnlyWithStock: true, BinIds: new[] { 9 });
        var occupancy = WarehouseRules.ParseOccupancy(new[] { "EMPTY,PARTIAL,FULL,NO_CAPACITY" }).Codes;
        var sql = WarehouseLayoutService.BinRowsQuery(db, 1, query, new[] { 5, 6 }, occupancy)
            .OrderBy(x => x.Bin.Code).ThenBy(x => x.Bin.WarehouseBinId).Skip(25).Take(25)
            .ToQueryString();
        Assert.Contains("OFFSET", sql);
        Assert.Contains("[MaxCapacityQty]", sql);
        Assert.Contains("[Aisle]", sql);
        Assert.Contains("[Position]", sql);
    }

    [Fact]
    public void Zone_occupancy_is_one_grouped_query_in_sql_server()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var sql = WarehouseLayoutService.ZoneStatsQuery(db, 1, null).ToQueryString();
        Assert.Contains("GROUP BY", sql);
        Assert.Contains("LEFT JOIN", sql);
        Assert.Contains("[MaxCapacityQty]", sql);
        Assert.Contains("GROUP BY", WarehouseLayoutService.ZoneStatsQuery(db, 1, 4).ToQueryString());
    }

    // ================================================================ modelo y SQL de estructura

    [Fact]
    public void Max_capacity_is_mapped_and_declared_in_the_structure_script()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var p = db.Model.FindEntityType(typeof(WarehouseBin))!.FindProperty(nameof(WarehouseBin.MaxCapacityQty))!;
        Assert.Equal("int", p.GetColumnType());
        Assert.True(p.IsNullable);

        var sql = File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-estructura.sql"));
        Assert.Contains("CONSTRAINT CK_WarehouseBin_MaxCapacityQty CHECK (MaxCapacityQty IS NULL OR MaxCapacityQty > 0)", sql);
        // Aditivo e idempotente sobre una base existente: columna y CHECK solo si faltan.
        Assert.Contains("IF OBJECT_ID('dbo.WarehouseBin') IS NULL", sql);
        Assert.Contains("ELSE IF COL_LENGTH('dbo.WarehouseBin', 'MaxCapacityQty') IS NULL", sql);
        Assert.Contains("ALTER TABLE dbo.WarehouseBin ADD MaxCapacityQty INT NULL;", sql);
        Assert.Contains("IF OBJECT_ID('dbo.CK_WarehouseBin_MaxCapacityQty', 'C') IS NULL", sql);
    }

    // ================================================================ utilidades

    private static async Task RunAsync(Func<World, Task> body)
    {
        await using var x = await WorldAsync();
        await body(x);
    }
}
