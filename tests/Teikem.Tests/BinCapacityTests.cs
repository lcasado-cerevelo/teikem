using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Migration;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Migration;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 11 — cupo máximo de las posiciones: (1) estimación pura desde el historial del WMS MSWM (máximo, redondeo a la decena,
/// mediana por pasillo → zona → almacén, origen) y la decisión de --update (nunca pisa un cupo existente); (2) el plan y el
/// reporte del importador (CSV -cupos y resumen por origen); (3) la asignación en bloque del API (filtros = los del listado,
/// fijar y quitar, 400 con los mensajes exactos, aislamiento por almacén y tenant, conteo) sobre InMemory (WmsFixture) y su
/// traducción a SQL Server sin BD.
/// </summary>
public sealed class BinCapacityTests
{
    // ================================================================ reglas puras: redondeo y mediana

    [Theory]
    [InlineData(0.5, 10)]
    [InlineData(1, 10)]
    [InlineData(10, 10)]
    [InlineData(10.2, 20)]
    [InlineData(11, 20)]
    [InlineData(50, 50)]
    [InlineData(1212, 1220)]
    [InlineData(264593, 264600)]
    public void Historical_max_rounds_up_to_the_ten_with_a_minimum_of_ten(double qty, int expected)
        => Assert.Equal(expected, BinCapacityRules.RoundUpToStep((decimal)qty));

    [Fact]
    public void Capacity_is_capped_to_the_largest_ten_that_fits_in_an_int()
        => Assert.Equal(2_147_483_640, BinCapacityRules.RoundUpToStep(10_000_000_000m));

    [Theory]
    [InlineData(new int[0], null)]
    [InlineData(new[] { 40 }, 40)]
    [InlineData(new[] { 90, 10, 30 }, 30)]
    [InlineData(new[] { 10, 20, 30, 40 }, 30)]   // (20 + 30) / 2 = 25 → 30
    [InlineData(new[] { 20, 40 }, 30)]
    [InlineData(new[] { 10, 10, 50, 1000 }, 30)]
    public void Median_takes_the_middle_and_rounds_up_to_the_ten(int[] values, int? expected)
        => Assert.Equal(expected, BinCapacityRules.Median(values));

    [Fact]
    public void Historical_max_is_the_largest_positive_photo_per_bin_code_case_insensitive()
    {
        var max = BinCapacityRules.HistoricalMaxByCode(new (string, decimal)[]
        {
            ("01-a-24", 12), ("01-A-24", 30), ("01-A-24", 7),      // misma posición al pasarla a mayúsculas
            ("MATTRESS PISO DEPOT", 5),                            // espacios → guiones, como el código de la posición
            ("02-B-03", 0), ("02-B-03", -4),                       // ≤ 0 no cuenta
            ("  ", 99),
        });
        Assert.Equal(new Dictionary<string, decimal> { ["01-A-24"] = 30m, ["MATTRESS-PISO-DEPOT"] = 5m }, max);
    }

    // ================================================================ reglas puras: estimación por origen

    private static readonly BinCapacityInput[] Bins =
    {
        new("01-A-01", "PCK", "01"), new("01-A-02", "PCK", "01"), new("01-B-01", "PCK", "01"), new("01-B-02", "PCK", "01"),
        new("02-A-01", "PCK", "02"),                    // pasillo sin historial → mediana de la zona PCK
        new("25-A-01", "ALM", "25"), new("25-A-02", "ALM", "25"),   // zona sin historial → mediana del almacén
        new("PISO", "PISO", null), new("CARTONES", "PISO", null),   // sin pasillo: salta a su zona
    };

    private static readonly Dictionary<string, decimal> History = new()
    {
        ["01-A-01"] = 12m,    // → 20
        ["01-A-02"] = 37m,    // → 40
        ["01-B-01"] = 55m,    // → 60
        ["CARTONES"] = 4500m, // → 4500
    };

    [Fact]
    public void Estimate_uses_own_history_then_aisle_zone_and_warehouse_medians()
    {
        // minSamples: 1 = la cadena completa con pocos datos (la regla real exige MinSamples; ver la prueba siguiente)
        var byCode = BinCapacityRules.Estimate(Bins, History, minSamples: 1).ToDictionary(e => e.Code);

        Assert.Equal(Bins.Length, byCode.Count);
        Assert.Equal(new BinCapacityEstimate("01-A-01", "PCK", "01", 12m, 20, BinCapacityOrigins.History), byCode["01-A-01"]);
        Assert.Equal((40, BinCapacityOrigins.History), (byCode["01-A-02"].Capacity, byCode["01-A-02"].Origin));
        Assert.Equal((4500, BinCapacityOrigins.History), (byCode["CARTONES"].Capacity, byCode["CARTONES"].Origin));
        // Pasillo 01: mediana de 20, 40, 60 = 40.
        Assert.Equal(new BinCapacityEstimate("01-B-02", "PCK", "01", null, 40, BinCapacityOrigins.Aisle), byCode["01-B-02"]);
        // Pasillo 02 sin historial → zona PCK: mediana de 20, 40, 60 = 40.
        Assert.Equal((40, BinCapacityOrigins.Zone), (byCode["02-A-01"].Capacity, byCode["02-A-01"].Origin));
        // PISO sin pasillo → zona PISO: solo CARTONES (4500).
        Assert.Equal((4500, BinCapacityOrigins.Zone, (decimal?)null), (byCode["PISO"].Capacity, byCode["PISO"].Origin, byCode["PISO"].HistoricalMax));
        // Zona ALM sin historial → almacén: mediana de 20, 40, 60, 4500 = (40 + 60) / 2 = 50.
        Assert.Equal((50, BinCapacityOrigins.Warehouse), (byCode["25-A-01"].Capacity, byCode["25-A-01"].Origin));
        Assert.Equal((50, BinCapacityOrigins.Warehouse), (byCode["25-A-02"].Capacity, byCode["25-A-02"].Origin));
    }

    [Fact]
    public void Estimate_skips_aisle_and_zone_medians_with_fewer_than_MinSamples_values()
    {
        // Caso real de Depot: la zona PISO solo tenía dos posiciones con historial (30 y 264.600); su "mediana" (132.320) no
        // representa a nadie, así que las posiciones de piso sin historial toman la mediana del almacén.
        var bins = new[]
        {
            new BinCapacityInput("CARTONES", "PISO", null), new BinCapacityInput("PISO", "PISO", null), new BinCapacityInput("FLOOR", "PISO", null),
            new BinCapacityInput("01-A-01", "PCK", "01"), new BinCapacityInput("01-A-02", "PCK", "01"), new BinCapacityInput("01-A-03", "PCK", "01"),
            new BinCapacityInput("01-A-04", "PCK", "01"), new BinCapacityInput("01-A-05", "PCK", "01"), new BinCapacityInput("01-A-06", "PCK", "01"),
        };
        var history = new Dictionary<string, decimal> { ["CARTONES"] = 264_600m, ["PISO"] = 30m, ["01-A-01"] = 10m, ["01-A-02"] = 20m, ["01-A-03"] = 30m, ["01-A-04"] = 40m, ["01-A-05"] = 50m };
        var byCode = BinCapacityRules.Estimate(bins, history).ToDictionary(e => e.Code);
        // pasillo 01 con 5 datos: su mediana sí se usa
        Assert.Equal((30, BinCapacityOrigins.Aisle), (byCode["01-A-06"].Capacity, byCode["01-A-06"].Origin));
        // zona PISO con 2 datos: se salta a la mediana del almacén (10, 20, 30, 30, 40, 50, 264.600 → 30)
        Assert.Equal((30, BinCapacityOrigins.Warehouse), (byCode["FLOOR"].Capacity, byCode["FLOOR"].Origin));
    }

    [Fact]
    public void Estimate_keeps_the_input_order_and_only_history_feeds_the_medians()
    {
        var list = BinCapacityRules.Estimate(Bins, History, minSamples: 1);
        Assert.Equal(Bins.Select(b => b.Code), list.Select(e => e.Code));
        // Si los cupos heredados alimentaran la mediana del almacén, 25-A-0x cambiaría; se calcula con los 4 de HISTORIAL.
        Assert.Equal(4, list.Count(e => e.Origin == BinCapacityOrigins.History));
        Assert.All(list.Where(e => e.Origin != BinCapacityOrigins.History), e => Assert.Null(e.HistoricalMax));
    }

    [Fact]
    public void Estimate_ignores_history_of_bins_that_are_not_in_the_plan_and_is_empty_without_history()
    {
        Assert.Empty(BinCapacityRules.Estimate(Bins, new Dictionary<string, decimal>()));
        Assert.Empty(BinCapacityRules.Estimate(Bins, new Dictionary<string, decimal> { ["W1"] = 80m, ["01-A-01"] = 0m }));
    }

    [Theory]
    [InlineData(false, null, false, BinCapacityAction.AssignOnCreate)]
    [InlineData(false, null, true, BinCapacityAction.AssignOnCreate)]
    [InlineData(true, 25, false, BinCapacityAction.KeepExisting)]
    [InlineData(true, 25, true, BinCapacityAction.KeepExisting)]      // --update NUNCA pisa un cupo existente
    [InlineData(true, null, true, BinCapacityAction.FillExisting)]    // --update llena solo los NULL
    [InlineData(true, null, false, BinCapacityAction.SkipWithoutUpdate)]
    public void Decide_never_overwrites_an_existing_capacity(bool exists, int? current, bool update, BinCapacityAction expected)
        => Assert.Equal(expected, BinCapacityRules.Decide(exists, current, update));

    [Fact]
    public void Result_texts_of_the_capacity_csv_are_exact()
    {
        Assert.Equal("Asignado al crear la posición", LegacyImportPlanner.CapacityResult(BinCapacityAction.AssignOnCreate, null, dryRun: false));
        Assert.Equal("Se asignaría al crear la posición", LegacyImportPlanner.CapacityResult(BinCapacityAction.AssignOnCreate, null, dryRun: true));
        Assert.Equal("Asignado (--update: la posición no tenía cupo)", LegacyImportPlanner.CapacityResult(BinCapacityAction.FillExisting, null, false));
        Assert.Equal("Se asignaría (--update: la posición no tenía cupo)", LegacyImportPlanner.CapacityResult(BinCapacityAction.FillExisting, null, true));
        Assert.Equal("Se conserva el cupo actual (25)", LegacyImportPlanner.CapacityResult(BinCapacityAction.KeepExisting, 25, false));
        Assert.Equal("Sin cambio: la posición ya existía sin cupo (use --update para llenarlo)",
            LegacyImportPlanner.CapacityResult(BinCapacityAction.SkipWithoutUpdate, null, true));
    }

    // ================================================================ plan y reporte del importador

    private const string WmsConfigJson = """
        {
          "company": { "name": "Prueba WMS" },
          "sources": { "products": "p.csv", "mswm": { "connectionStringName": "LegacyMswm", "warehouseId": "Main" } },
          "warehouse": {
            "code": "alm-depot", "name": "Almacén Depot",
            "zones": [
              { "code": "PCK", "name": "Picking", "zoneType": "PICKING", "matchDescription": "Picking Location" },
              { "code": "PISO", "name": "Piso", "zoneType": "RESERVE", "matchLocationIds": ["PISO", "CARTONES"] }
            ]
          },
          "openingBalances": { "source": "mswm" }
        }
        """;

    private static LegacyImportConfig Config(string json) => LegacyImportConfig.Parse(json, Path.GetTempPath(), checkFiles: false);

    private static LegacyImportSources WmsSources(IReadOnlyList<WmsBinQuantity>? history) => new(
        Array.Empty<QbItem>(), Array.Empty<QbItem>(), Array.Empty<QbCustomer>(), Array.Empty<QbVendor>(), Array.Empty<WmsItem>(),
        new[]
        {
            new WmsLocation("01-a-01", 1, "Picking Location", false), new WmsLocation("01-A-02", 1, "Picking Location", false),
            new WmsLocation("01-A-03", 1, "Picking Location", false), new WmsLocation("PISO", 3, null, false),
            new WmsLocation("CARTONES", 3, null, false),
        },
        Array.Empty<WmsInventoryRow>(), Array.Empty<WmsUpc>(), history);

    private static readonly WmsBinQuantity[] SampleHistory =
    {
        new(WmsBinHistorySources.Inventory, "01-A-01", 8), new(WmsBinHistorySources.PutAwayHistory, "01-a-01", 33),
        new(WmsBinHistorySources.CycleCountInventory, "01-A-02", 71), new(WmsBinHistorySources.InventoryOld, "CARTONES", 4500),
        new(WmsBinHistorySources.CycleCountHistory, "W1", 999),   // posición que no se migra: no cuenta
    };

    [Fact]
    public void Planner_estimates_capacities_of_the_wms_bins_and_reports_the_count_by_origin()
    {
        var report = new LegacyImportReport("Prueba WMS", dryRun: true);
        var plan = LegacyImportPlanner.Build(Config(WmsConfigJson), WmsSources(SampleHistory), report);

        Assert.Equal(
            new[]
            {
                ("01-A-01", 40, BinCapacityOrigins.History), ("01-A-02", 80, BinCapacityOrigins.History),
                ("01-A-03", 80, BinCapacityOrigins.Warehouse), ("PISO", 80, BinCapacityOrigins.Warehouse), ("CARTONES", 4500, BinCapacityOrigins.History),
            },
            plan.Capacities.Select(c => (c.Code, c.Capacity, c.Origin)));
        Assert.Equal(33m, plan.Capacities[0].HistoricalMax);
        Assert.Contains(report.Info, i => i.Key == "Cupos estimados" && i.Value == "HISTORIAL 3, PASILLO 0, ZONA 0, ALMACEN 2");
    }

    [Fact]
    public void Planner_does_not_estimate_without_mswm_history_and_warns_when_there_is_none()
    {
        var noMswm = LegacyImportPlanner.Build(Config(WmsConfigJson), WmsSources(null), new LegacyImportReport("x", true));
        Assert.Empty(noMswm.Capacities);

        var report = new LegacyImportReport("x", true);
        var empty = LegacyImportPlanner.Build(Config(WmsConfigJson), WmsSources(Array.Empty<WmsBinQuantity>()), report);
        Assert.Empty(empty.Capacities);
        Assert.Contains(report.Advertencias, w => w.Message == "Ninguna posición de ALM-DEPOT tiene historial de existencias en el WMS; las posiciones quedan sin cupo.");
    }

    [Fact]
    public void Single_bin_warehouse_without_wms_never_gets_an_estimate()
    {
        var json = """
            {
              "company": { "name": "Advance Solutions" },
              "sources": { "products": "s.csv" },
              "warehouse": { "code": "ALM-SOL", "singleBin": { "zone": { "code": "GEN", "name": "General", "zoneType": "RESERVE" }, "bin": "GENERAL" } }
            }
            """;
        var plan = LegacyImportPlanner.Build(Config(json), WmsSources(SampleHistory), new LegacyImportReport("x", true));
        Assert.Single(plan.Bins);
        Assert.Empty(plan.Capacities);
    }

    [Fact]
    public void Report_writes_the_capacity_csv_and_the_summary_by_origin()
    {
        var report = new LegacyImportReport("Prueba WMS", dryRun: true);
        var plan = LegacyImportPlanner.Build(Config(WmsConfigJson), WmsSources(SampleHistory), report);
        foreach (var e in plan.Capacities) report.AddCapacity(e, LegacyImportPlanner.CapacityWouldAssign);

        var csv = report.RenderCsv().Single(c => c.Suffix == "cupos").Content;
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Posicion,Zona,Pasillo,MaximoHistorico,Cupo,Origen,Resultado", lines[0]);
        Assert.Equal("01-A-01,PCK,01,33,40,HISTORIAL,Se asignaría al crear la posición", lines[1]);
        Assert.Equal("01-A-03,PCK,01,,80,ALMACEN,Se asignaría al crear la posición", lines[3]);
        Assert.Equal("PISO,PISO,,,80,ALMACEN,Se asignaría al crear la posición", lines[4]);
        Assert.Equal(6, lines.Length);

        var md = report.RenderMarkdown();
        Assert.Contains("## Cupos de posición estimados (5 posiciones)", md);
        Assert.Contains("| HISTORIAL | 3 | 40 | 80 | 4500 |", md);
        Assert.Contains("| PASILLO | 0 | — | — | — |", md);
        Assert.Contains("| ALMACEN | 2 | 80 | 80 | 80 |", md);
        Assert.Contains("| **Total** | **5** | 40 | 80 | 4500 |", md);
        Assert.Contains("| Se asignaría al crear la posición | 5 |", md);
    }

    [Fact]
    public void Report_without_capacities_has_no_capacity_csv_nor_section()
    {
        var report = new LegacyImportReport("x", true);
        Assert.DoesNotContain(report.RenderCsv(), c => c.Suffix == "cupos");
        Assert.DoesNotContain("Cupos de posición", report.RenderMarkdown());
    }

    // ================================================================ asignación en bloque (servicio, InMemory)

    private sealed record World(WmsFixture F, Warehouse W, WarehouseZone Pck, WarehouseZone Rsv, WarehouseBin P01, WarehouseBin P02,
        WarehouseBin P03, WarehouseBin R01, WarehouseBin R02) : IAsyncDisposable
    {
        public WarehouseLayoutService Layout => F.Get<WarehouseLayoutService>();
        public ValueTask DisposeAsync() => F.DisposeAsync();
    }

    /// <summary>PCK: P-01 (pasillo 01) sin cupo, P-02 (pasillo 01) cupo 30, P-03 (pasillo 02) sin cupo. RSV: R-01 cupo 20, R-02 inactiva.</summary>
    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s => s.AddSingleton<WarehouseLayoutService>());
        var w = await f.AddWarehouseAsync("W1");
        var pck = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var rsv = await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve);
        var p01 = await f.AddBinAsync(pck, "P-01", aisle: "01");
        var p02 = await f.AddBinAsync(pck, "P-02", maxCapacityQty: 30, aisle: "01");
        var p03 = await f.AddBinAsync(pck, "P-03", aisle: "02");
        var r01 = await f.AddBinAsync(rsv, "R-01", maxCapacityQty: 20);
        var r02 = await f.AddBinAsync(rsv, "R-02", isActive: false);
        f.Db.ChangeTracker.Clear();
        return new World(f, w, pck, rsv, p01, p02, p03, r01, r02);
    }

    private static async Task RunAsync(Func<World, Task> body)
    {
        await using var x = await WorldAsync();
        await body(x);
    }

    private static async Task<Dictionary<string, int?>> CapacitiesAsync(World x)
    {
        x.F.Db.ChangeTracker.Clear();
        return await x.F.Db.WarehouseBins.AsNoTracking().Where(b => b.WarehouseId == x.W.WarehouseId).ToDictionaryAsync(b => b.Code, b => b.MaxCapacityQty);
    }

    [Fact]
    public Task Sets_the_capacity_of_every_bin_of_the_zone_and_counts_only_real_changes()
        => RunAsync(async x =>
        {
            var r = await x.Layout.SetBinsCapacityAsync(x.W.PublicId, new WarehouseBinCapacityRequest(ZoneIds: new[] { x.Pck.WarehouseZoneId }, MaxCapacityQty: 30), default);
            Assert.Equal(new WarehouseBinCapacityResultDto(3, 2), r);   // P-02 ya tenía 30
            var caps = await CapacitiesAsync(x);
            Assert.Equal(((int?)30, (int?)30, (int?)30, (int?)20, (int?)null), (caps["P-01"], caps["P-02"], caps["P-03"], caps["R-01"], caps["R-02"]));

            // Repetir no cambia nada.
            Assert.Equal(new WarehouseBinCapacityResultDto(3, 0),
                await x.Layout.SetBinsCapacityAsync(x.W.PublicId, new WarehouseBinCapacityRequest(ZoneIds: new[] { x.Pck.WarehouseZoneId }, MaxCapacityQty: 30), default));
        });

    [Fact]
    public Task Clear_removes_the_capacity_of_the_filtered_bins()
        => RunAsync(async x =>
        {
            var r = await x.Layout.SetBinsCapacityAsync(x.W.PublicId, new WarehouseBinCapacityRequest(Search: "p-0", Clear: true), default);
            Assert.Equal(new WarehouseBinCapacityResultDto(3, 1), r);
            var caps = await CapacitiesAsync(x);
            Assert.Null(caps["P-02"]);
            Assert.Equal(20, caps["R-01"]);
        });

    [Fact]
    public Task Filters_are_the_ones_of_the_bin_list_and_the_list_total_previews_the_count()
        => RunAsync(async x =>
        {
            async Task Same(WarehouseBinQuery listQuery, WarehouseBinCapacityRequest bulk)
            {
                var preview = await x.Layout.ListBinsAsync(x.W.PublicId, listQuery with { Take = 1 }, default);
                var r = await x.Layout.SetBinsCapacityAsync(x.W.PublicId, bulk, default);
                Assert.Equal(preview.Total, r.Matched);
            }
            await Same(new WarehouseBinQuery(Aisle: "01"), new WarehouseBinCapacityRequest(Aisle: "01", MaxCapacityQty: 15));
            await Same(new WarehouseBinQuery(Search: "rsv"), new WarehouseBinCapacityRequest(Search: "rsv", MaxCapacityQty: 15));
            await Same(new WarehouseBinQuery(Search: "rsv", IncludeInactive: true), new WarehouseBinCapacityRequest(Search: "rsv", IncludeInactive: true, MaxCapacityQty: 15));
            await Same(new WarehouseBinQuery(BinIds: new[] { x.P03.WarehouseBinId, x.R02.WarehouseBinId }),
                new WarehouseBinCapacityRequest(BinIds: new[] { x.P03.WarehouseBinId, x.R02.WarehouseBinId }, MaxCapacityQty: 15));

            var caps = await CapacitiesAsync(x);
            // Pasillo 01 (P-01, P-02), RSV con inactivas (R-01, R-02) y P-03 por id: todas en 15.
            Assert.All(caps.Values, c => Assert.Equal(15, c));
        });

    [Fact]
    public Task Inactive_bins_are_excluded_unless_asked()
        => RunAsync(async x =>
        {
            Assert.Equal(new WarehouseBinCapacityResultDto(1, 1),
                await x.Layout.SetBinsCapacityAsync(x.W.PublicId, new WarehouseBinCapacityRequest(ZoneIds: new[] { x.Rsv.WarehouseZoneId }, MaxCapacityQty: 50), default));
            Assert.Null((await CapacitiesAsync(x))["R-02"]);
            Assert.Equal(new WarehouseBinCapacityResultDto(2, 1),
                await x.Layout.SetBinsCapacityAsync(x.W.PublicId, new WarehouseBinCapacityRequest(ZoneIds: new[] { x.Rsv.WarehouseZoneId }, IncludeInactive: true, MaxCapacityQty: 50), default));
        });

    [Fact]
    public Task Only_without_capacity_never_overwrites_an_existing_one()
        => RunAsync(async x =>
        {
            // Es lo que usa import-legacy --update: las posiciones con cupo (P-02 30, R-01 20) no se tocan.
            var r = await x.Layout.SetBinsCapacityAsync(x.W.PublicId, new WarehouseBinCapacityRequest(AllBins: true, IncludeInactive: true,
                OnlyWithoutCapacity: true, MaxCapacityQty: 70), default);
            Assert.Equal(new WarehouseBinCapacityResultDto(3, 3), r);
            var caps = await CapacitiesAsync(x);
            Assert.Equal(((int?)70, (int?)30, (int?)70, (int?)20, (int?)70), (caps["P-01"], caps["P-02"], caps["P-03"], caps["R-01"], caps["R-02"]));
        });

    [Fact]
    public Task All_bins_requires_the_explicit_flag()
        => RunAsync(async x =>
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                x.Layout.SetBinsCapacityAsync(x.W.PublicId, new WarehouseBinCapacityRequest(MaxCapacityQty: 10, Aisle: "  ", ZoneIds: Array.Empty<int>()), default));
            Assert.Equal("Indique al menos un filtro de posiciones (zoneIds, aisle, rack, level, position, search o binIds) o allBins: true para aplicarlo a todo el almacén.",
                Assert.Single(ex.Errors!["allBins"]));
            Assert.Equal(new WarehouseBinCapacityResultDto(4, 4),
                await x.Layout.SetBinsCapacityAsync(x.W.PublicId, new WarehouseBinCapacityRequest(AllBins: true, MaxCapacityQty: 10), default));
        });

    [Theory]
    [InlineData(null, false, "maxCapacityQty", "Indique el cupo máximo (maxCapacityQty) o clear: true para quitarlo.")]
    [InlineData(10, true, "clear", "Indique el cupo máximo o clear: true, no ambos.")]
    [InlineData(0, false, "maxCapacityQty", "El cupo máximo de la posición debe ser mayor que cero.")]
    [InlineData(-5, false, "maxCapacityQty", "El cupo máximo de la posición debe ser mayor que cero.")]
    public Task Value_must_be_exactly_one_of_capacity_or_clear(int? qty, bool clear, string field, string message)
        => RunAsync(async x =>
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                x.Layout.SetBinsCapacityAsync(x.W.PublicId, new WarehouseBinCapacityRequest(ZoneIds: new[] { x.Pck.WarehouseZoneId }, MaxCapacityQty: qty, Clear: clear), default));
            Assert.Equal(message, Assert.Single(ex.Errors![field]));
            Assert.Equal(((int?)null, (int?)30), ((await CapacitiesAsync(x))["P-01"], (await CapacitiesAsync(x))["P-02"]));   // nada cambió
        });

    [Fact]
    public void Validation_reports_value_and_filter_errors_together()
    {
        var errors = WarehouseRules.ValidateBulkCapacity(hasFilter: false, allBins: false, maxCapacityQty: null, clear: false);
        Assert.Equal(new[] { "allBins", "maxCapacityQty" }, errors.Keys.OrderBy(k => k));
        Assert.Empty(WarehouseRules.ValidateBulkCapacity(hasFilter: true, allBins: false, maxCapacityQty: 1, clear: false));
        Assert.Empty(WarehouseRules.ValidateBulkCapacity(hasFilter: false, allBins: true, maxCapacityQty: null, clear: true));
        Assert.False(WarehouseRules.HasBinFilter(Array.Empty<int>(), " ", null, "", null, null, Array.Empty<int>()));
        Assert.True(WarehouseRules.HasBinFilter(null, null, null, null, null, "a", null));
        Assert.True(WarehouseRules.HasBinFilter(null, null, null, null, null, null, new[] { 3 }));
    }

    [Fact]
    public Task Bins_and_zones_of_another_warehouse_are_never_touched()
        => RunAsync(async x =>
        {
            var w2 = await x.F.AddWarehouseAsync("W2");
            var z2 = await x.F.AddZoneAsync(w2, "PCK", ZoneTypes.Picking);
            var other = await x.F.AddBinAsync(z2, "P-01", aisle: "01");
            x.F.Db.ChangeTracker.Clear();

            // Ids de posición y de zona de W2 enviados contra W1: no cumplen el filtro (mismo criterio que el listado).
            Assert.Equal(new WarehouseBinCapacityResultDto(0, 0), await x.Layout.SetBinsCapacityAsync(x.W.PublicId,
                new WarehouseBinCapacityRequest(BinIds: new[] { other.WarehouseBinId }, ZoneIds: new[] { z2.WarehouseZoneId }, MaxCapacityQty: 99), default));
            // Todo W1 (allBins) no alcanza a W2.
            await x.Layout.SetBinsCapacityAsync(x.W.PublicId, new WarehouseBinCapacityRequest(AllBins: true, MaxCapacityQty: 99), default);
            x.F.Db.ChangeTracker.Clear();
            Assert.Null((await x.F.Db.WarehouseBins.AsNoTracking().SingleAsync(b => b.WarehouseBinId == other.WarehouseBinId)).MaxCapacityQty);
        });

    [Fact]
    public Task Warehouse_of_another_tenant_is_404_and_an_inactive_one_is_422()
        => RunAsync(async x =>
        {
            var foreign = await x.F.AddWarehouseAsync("WX", tenantId: WmsFixture.OtherTenantId);
            await Assert.ThrowsAsync<NotFoundException>(() => x.Layout.SetBinsCapacityAsync(foreign.PublicId,
                new WarehouseBinCapacityRequest(AllBins: true, MaxCapacityQty: 10), default));

            var closed = await x.F.AddWarehouseAsync("WC", isActive: false);
            var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Layout.SetBinsCapacityAsync(closed.PublicId,
                new WarehouseBinCapacityRequest(AllBins: true, MaxCapacityQty: 10), default));
            Assert.Equal(WarehouseRules.WarehouseInactiveMessage, ex.Message);
        });

    [Fact]
    public Task Chunks_cover_more_bins_than_one_query_loads()
        => RunAsync(async x =>
        {
            var bulk = await x.F.AddZoneAsync(x.W, "BLK", ZoneTypes.Reserve);
            for (var i = 0; i < WarehouseLayoutService.BulkCapacityChunk + 5; i++)
                x.F.Db.WarehouseBins.Add(new WarehouseBin { WarehouseBinId = 50_000 + i, WarehouseZoneId = bulk.WarehouseZoneId, WarehouseId = x.W.WarehouseId, Code = $"B-{i:0000}", IsActive = true });
            await x.F.Db.SaveChangesAsync();
            x.F.Db.ChangeTracker.Clear();

            var n = WarehouseLayoutService.BulkCapacityChunk + 5;
            Assert.Equal(new WarehouseBinCapacityResultDto(n, n),
                await x.Layout.SetBinsCapacityAsync(x.W.PublicId, new WarehouseBinCapacityRequest(ZoneIds: new[] { bulk.WarehouseZoneId }, MaxCapacityQty: 12), default));
            x.F.Db.ChangeTracker.Clear();
            Assert.Equal(n, await x.F.Db.WarehouseBins.CountAsync(b => b.WarehouseZoneId == bulk.WarehouseZoneId && b.MaxCapacityQty == 12));
        });

    // ================================================================ traducción a SQL Server (sin BD)

    [Fact]
    public void Bulk_capacity_queries_translate_to_sql_server()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var ids = WarehouseLayoutService.BinRowsQuery(db, 1, new WarehouseBinQuery(Search: "a", ZoneIds: new[] { 1 }, Aisle: "01", BinIds: new[] { 5 }), null, null)
            .Where(x => x.Bin.MaxCapacityQty == null)
            .Select(x => x.Bin.WarehouseBinId)
            .ToQueryString();
        Assert.Contains("[MaxCapacityQty] IS NULL", ids);
        Assert.Contains("[Aisle]", ids);

        var chunk = new[] { 1, 2, 3 };
        var load = db.WarehouseBins.Where(x => x.WarehouseId == 1 && chunk.Contains(x.WarehouseBinId)).ToQueryString();
        Assert.Contains("[WarehouseBinId]", load);
    }
}
