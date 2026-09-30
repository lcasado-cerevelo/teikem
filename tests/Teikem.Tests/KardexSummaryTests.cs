using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 14 (P0) — Kárdex en el backend: resumen con la perspectiva de SignedQuantity (espejo puro KardexRules.Summarize y la
/// consulta agrupada), filtros nuevos (dueño y propio, motivos, dirección IN/OUT, almacén de origen y de destino, solo
/// manuales), fechas en días LOCALES de Puerto Rico (borde de medianoche), dueño y categoría en la fila, y la traducción a SQL
/// Server de la consulta compartida y del agrupado (ToQueryString, sin BD).
/// </summary>
public class KardexSummaryTests
{
    private const int ClientA = 50;

    private sealed record World(InventoryServiceFixture F, Warehouse W1, Warehouse W2, WarehouseBin A1, WarehouseBin A2, WarehouseBin B1,
        Product Pa, Product Own);

    /// <summary>
    /// PA (cliente A, categoría Glucosa) y POWN (propio). Movimientos (UTC):
    /// 1. RECEIPT PA +10 → W1/A1 (recibo)            2. TRANSFER PA 4 W1/A1 → W1/A2 (manual)
    /// 3. TRANSFER PA 3 W1/A2 → W2/B1 (manual)       4. ADJUSTMENT PA −1 desde W1/A1, DAMAGE (manual)
    /// 5. ISSUE PA −2 desde W2/B1 (recolección)       6. RECEIPT POWN +5 → W1/A1 (manual)
    /// 7. ADJUSTMENT POWN +2 → W1/A1, FOUND, el 21/09 03:30 UTC = 20/09 23:30 en Puerto Rico.
    /// </summary>
    private static async Task<World> SeedAsync()
    {
        var f = await InventoryServiceFixture.CreateAsync();
        await f.AddClientAsync(ClientA, "Cliente A");
        var cat = await f.AddCategoryAsync("Glucosa");
        var w1 = await f.AddWarehouseAsync("ALM-01");
        var w2 = await f.AddWarehouseAsync("ALM-02");
        var z1 = await f.AddZoneAsync(w1, "PCK", "PICKING");
        var z2 = await f.AddZoneAsync(w2, "RES", "RESERVE");
        var a1 = await f.AddBinAsync(z1, "A-01");
        var a2 = await f.AddBinAsync(z1, "A-02");
        var b1 = await f.AddBinAsync(z2, "B-01");
        var pa = await f.AddProductAsync("PA", "NONE", ClientA, cat.ProductCategoryId);
        var own = await f.AddProductAsync("POWN", "NONE");
        var batch = await f.AddPickBatchAsync(w2, "EMP-00010", null);

        var t0 = new DateTime(2026, 9, 20, 14, 0, 0, DateTimeKind.Utc);
        await f.AddTxnAsync("RECEIPT", pa, 10m, to: (w1, a1), at: t0, refEntity: EntityTypes.Receipt, refId: 1);
        await f.AddTxnAsync("TRANSFER", pa, 4m, from: (w1, a1), to: (w1, a2), at: t0.AddHours(1));
        await f.AddTxnAsync("TRANSFER", pa, 3m, from: (w1, a2), to: (w2, b1), at: t0.AddHours(2));
        await f.AddTxnAsync("ADJUSTMENT", pa, -1m, from: (w1, a1), at: t0.AddHours(3), reason: "DAMAGE");
        await f.AddTxnAsync("ISSUE", pa, -2m, from: (w2, b1), at: t0.AddHours(4), refEntity: EntityTypes.PickBatch, refId: batch.PickBatchId);
        await f.AddTxnAsync("RECEIPT", own, 5m, to: (w1, a1), at: t0.AddHours(5));
        await f.AddTxnAsync("ADJUSTMENT", own, 2m, to: (w1, a1), at: new DateTime(2026, 9, 21, 3, 30, 0, DateTimeKind.Utc), reason: "FOUND");
        return new World(f, w1, w2, a1, a2, b1, pa, own);
    }

    private static async Task<(int Total, KardexSummaryDto Summary)> BothAsync(InventoryReadService reads, KardexQuery q)
    {
        var page = await reads.KardexAsync(q, InventoryScope.Any, default);
        var summary = await reads.KardexSummaryAsync(q, InventoryScope.Any, default);
        Assert.Equal(page.Total, summary.Movements);   // el resumen usa la misma consulta que la lista
        return (page.Total, summary);
    }

    // ---------------------------------------------------------------- reglas puras

    [Fact]
    public void Summarize_without_location_filter_counts_a_transfer_as_internal()
    {
        var groups = new[]
        {
            new KardexSummaryGroup(InventoryTxnTypes.Receipt, null, null, 1, 10, 10m, 2),
            new KardexSummaryGroup(InventoryTxnTypes.Transfer, 1, 10, 1, 11, 4m, 1),
            new KardexSummaryGroup(InventoryTxnTypes.Issue, 1, 11, null, null, -3m, 3),
            new KardexSummaryGroup(InventoryTxnTypes.Adjustment, 1, 10, null, null, -1m, 1),
        };
        Assert.Equal(new KardexSummary(7, 2, 10m, 4, 4m, 1), KardexRules.Summarize(groups, KardexLocationFilter.None));
    }

    [Fact]
    public void Summarize_with_a_bin_filter_turns_a_transfer_into_in_out_or_internal()
    {
        var transfer = new KardexSummaryGroup(InventoryTxnTypes.Transfer, 1, 10, 1, 11, 4m, 1);
        var fromBin = new KardexLocationFilter(null, new HashSet<int> { 10 });
        var toBin = new KardexLocationFilter(null, new HashSet<int> { 11 });
        var both = new KardexLocationFilter(null, new HashSet<int> { 10, 11 });
        Assert.Equal(new KardexSummary(1, 0, 0m, 1, 4m, 0), KardexRules.Summarize(new[] { transfer }, fromBin));
        Assert.Equal(new KardexSummary(1, 1, 4m, 0, 0m, 0), KardexRules.Summarize(new[] { transfer }, toBin));
        Assert.Equal(new KardexSummary(1, 0, 0m, 0, 0m, 1), KardexRules.Summarize(new[] { transfer }, both));
    }

    [Theory]
    [InlineData("in", "IN")]
    [InlineData(" OUT ", "OUT")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Direction_is_normalized(string? input, string? expected)
    {
        var (direction, error) = KardexRules.NormalizeDirection(input);
        Assert.Equal(expected, direction);
        Assert.Null(error);
    }

    [Fact]
    public void Unknown_direction_has_the_exact_message()
    {
        Assert.Equal("La dirección debe ser IN (entradas) u OUT (salidas).", KardexRules.NormalizeDirection("X").Error);
        Assert.Equal(KardexRules.DirectionIn, KardexRules.DirectionOf(1m));
        Assert.Equal(KardexRules.DirectionOut, KardexRules.DirectionOf(-0.5m));
        Assert.Null(KardexRules.DirectionOf(0m));
    }

    // ---------------------------------------------------------------- servicio

    [Fact]
    public async Task Summary_matches_the_list_without_and_with_a_warehouse_filter()
    {
        var w = await SeedAsync();
        var reads = w.F.Get<InventoryReadService>();

        var (total, all) = await BothAsync(reads, new KardexQuery());
        Assert.Equal(7, total);
        Assert.Equal(new KardexSummaryDto(7, 3, 17m, 2, 3m, 2), all);

        // Desde la perspectiva de ALM-01: la transferencia A-01 → A-02 es interna y la de A-02 → ALM-02 es salida.
        var (_, w1) = await BothAsync(reads, new KardexQuery(WarehousePublicIds: new[] { w.W1.PublicId }));
        Assert.Equal(new KardexSummaryDto(6, 3, 17m, 2, 4m, 1), w1);

        // In − Out == Σ signedQuantity de la lista con el mismo filtro.
        var page = await reads.KardexAsync(new KardexQuery(WarehousePublicIds: new[] { w.W1.PublicId }), InventoryScope.Any, default);
        Assert.Equal(w1.InQty - w1.OutQty, page.Items.Sum(i => i.SignedQuantity));
    }

    [Fact]
    public async Task Direction_filters_with_the_same_perspective_as_signed_quantity()
    {
        var w = await SeedAsync();
        var reads = w.F.Get<InventoryReadService>();
        Assert.Equal(3, (await BothAsync(reads, new KardexQuery(Direction: "IN"))).Total);
        Assert.Equal(2, (await BothAsync(reads, new KardexQuery(Direction: "out"))).Total);

        var inW1 = await reads.KardexAsync(new KardexQuery(WarehousePublicIds: new[] { w.W1.PublicId }, Direction: "IN"), InventoryScope.Any, default);
        Assert.Equal(3, inW1.Total);
        Assert.All(inW1.Items, i => Assert.True(i.SignedQuantity > 0));
        var outW1 = await reads.KardexAsync(new KardexQuery(WarehousePublicIds: new[] { w.W1.PublicId }, Direction: "OUT"), InventoryScope.Any, default);
        Assert.Equal(2, outW1.Total);
        Assert.All(outW1.Items, i => Assert.True(i.SignedQuantity < 0));
        // Por posición: A-02 recibe 4 (entrada) y entrega 3 (salida).
        var inA2 = await reads.KardexAsync(new KardexQuery(BinIds: new[] { w.A2.WarehouseBinId }, Direction: "IN"), InventoryScope.Any, default);
        Assert.Equal(4m, Assert.Single(inA2.Items).SignedQuantity);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => reads.KardexAsync(new KardexQuery(Direction: "X"), InventoryScope.Any, default));
        Assert.Equal("La dirección debe ser IN (entradas) u OUT (salidas).", ex.Errors!["direction"].Single());
        await Assert.ThrowsAsync<ValidationException>(() => reads.KardexSummaryAsync(new KardexQuery(Direction: "X"), InventoryScope.Any, default));
    }

    [Fact]
    public async Task Owner_filter_with_clients_and_own_products()
    {
        var w = await SeedAsync();
        var reads = w.F.Get<InventoryReadService>();
        var clientA = await w.F.Db.Clients.AsNoTracking().SingleAsync(c => c.ClientId == ClientA);

        Assert.Equal(5, (await BothAsync(reads, new KardexQuery(OwnerClientPublicIds: new[] { clientA.PublicId }))).Total);
        Assert.Equal(2, (await BothAsync(reads, new KardexQuery(IncludeOwn: true))).Total);
        Assert.Equal(7, (await BothAsync(reads, new KardexQuery(OwnerClientPublicIds: new[] { clientA.PublicId }, IncludeOwn: true))).Total);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            reads.KardexAsync(new KardexQuery(OwnerClientPublicIds: new[] { Guid.NewGuid() }), InventoryScope.Any, default));
        Assert.Equal("Cliente no encontrado.", ex.Message);

        // Cliente de otro tenant: 404 igual (sin oráculo).
        var other = await w.F.AddClientAsync(77, "Ajeno", InventoryServiceFixture.OtherTenantId);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            reads.KardexAsync(new KardexQuery(OwnerClientPublicIds: new[] { other.PublicId }), InventoryScope.Any, default));
    }

    [Fact]
    public async Task Reasons_origin_destination_and_manual_only()
    {
        var w = await SeedAsync();
        var reads = w.F.Get<InventoryReadService>();

        Assert.Equal(1, (await BothAsync(reads, new KardexQuery(Reasons: new[] { "damage" }))).Total);
        Assert.Equal(2, (await BothAsync(reads, new KardexQuery(Reasons: new[] { "DAMAGE,FOUND" }))).Total);
        var unknown = await Assert.ThrowsAsync<ValidationException>(() =>
            reads.KardexAsync(new KardexQuery(Reasons: new[] { "FOO" }), InventoryScope.Any, default));
        Assert.Equal("Motivo de ajuste desconocido: 'FOO'.", unknown.Errors!["reasons"].Single());

        Assert.Equal(3, (await BothAsync(reads, new KardexQuery(FromWarehousePublicIds: new[] { w.W1.PublicId }))).Total);
        Assert.Equal(1, (await BothAsync(reads, new KardexQuery(ToWarehousePublicIds: new[] { w.W2.PublicId }))).Total);
        var w1ToW2 = await reads.KardexAsync(new KardexQuery(FromWarehousePublicIds: new[] { w.W1.PublicId }, ToWarehousePublicIds: new[] { w.W2.PublicId }),
            InventoryScope.Any, default);
        Assert.Equal("ALM-01/A-02 → ALM-02/B-01", Assert.Single(w1ToW2.Items).Position);

        var manual = await reads.KardexAsync(new KardexQuery(ManualOnly: true), InventoryScope.Any, default);
        Assert.Equal(5, manual.Total);
        Assert.All(manual.Items, i => Assert.Null(i.RefEntityCode));
        Assert.Equal(5, (await reads.KardexSummaryAsync(new KardexQuery(ManualOnly: true), InventoryScope.Any, default)).Movements);
    }

    [Fact]
    public async Task Dates_are_local_puerto_rico_days()
    {
        var w = await SeedAsync();
        var reads = w.F.Get<InventoryReadService>();
        // El ajuste del 21/09 03:30 UTC es del 20/09 en Puerto Rico: entra en el 20 y no en el 21.
        var day20 = new DateOnly(2026, 9, 20);
        var day21 = new DateOnly(2026, 9, 21);
        Assert.Equal(7, (await BothAsync(reads, new KardexQuery(From: day20, To: day20))).Total);
        Assert.Equal(0, (await BothAsync(reads, new KardexQuery(From: day21, To: day21))).Total);
        Assert.Equal(7, (await BothAsync(reads, new KardexQuery(From: day20))).Total);
        var inverted = await Assert.ThrowsAsync<ValidationException>(() =>
            reads.KardexSummaryAsync(new KardexQuery(From: day21, To: day20), InventoryScope.Any, default));
        Assert.Equal(KardexRules.RangeInverted, inverted.Message);
    }

    [Fact]
    public async Task Rows_carry_owner_and_category()
    {
        var w = await SeedAsync();
        var page = await w.F.Get<InventoryReadService>().KardexAsync(new KardexQuery(), InventoryScope.Any, default);
        Assert.All(page.Items.Where(i => i.Sku == "PA"), i => Assert.Equal(("Cliente A", "Glucosa"), (i.OwnerName, i.CategoryName)));
        Assert.All(page.Items.Where(i => i.Sku == "POWN"), i => Assert.Equal(("Propio", (string?)null), (i.OwnerName, i.CategoryName)));
    }

    [Fact]
    public async Task Owners_are_own_plus_the_clients_that_own_products()
    {
        var w = await SeedAsync();
        await w.F.AddClientAsync(51, "Sin productos");
        var owners = await w.F.Get<InventoryReadService>().OwnersAsync(InventoryScope.Any, default);
        Assert.Equal(new[] { ("Propio", true), ("Cliente A", false) }, owners.Select(o => (o.Name, o.IsOwn)).ToArray());
        Assert.Null(owners[0].ClientPublicId);
        Assert.NotNull(owners[1].ClientPublicId);
        // Con scope de dueño (Portal) no hay "Propio".
        Assert.Equal("Cliente A", Assert.Single(await w.F.Get<InventoryReadService>().OwnersAsync(new InventoryScope(ClientA), default)).Name);
    }

    // ---------------------------------------------------------------- traducción a SQL Server (sin BD)

    [Fact]
    public async Task Shared_query_and_summary_groups_translate_to_sql_server()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var lookups = new TripTestLookups();
        var id = 1;
        LookupCode L(string entity, string code) => new() { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = "{}", IsActive = true };
        lookups.Load(new[]
        {
            L(LookupDomains.InventoryTxnType, InventoryTxnTypes.Transfer), L(LookupDomains.InventoryTxnType, InventoryTxnTypes.Adjustment),
            L(LookupDomains.AdjustmentReason, AdjustmentReasons.Damage), L(LookupDomains.EntityType, EntityTypes.Receipt),
        });
        var reads = new InventoryReadService(db, new TenantContext { TenantId = 1, UserId = 1 }, lookups, TenantClock.Default);

        foreach (var q in new[]
                 {
                     new KardexQuery(From: new DateOnly(2026, 9, 1), To: new DateOnly(2026, 9, 30), Types: new[] { "ADJUSTMENT" },
                         BinIds: new[] { 5, 6 }, Reasons: new[] { "DAMAGE" }, Direction: "IN", IncludeOwn: true, ManualOnly: true),
                     new KardexQuery(BinIds: new[] { 5 }, Direction: "OUT", LotNumber: "L-1", SerialNumber: "S-1", Search: "abc"),
                     new KardexQuery(Direction: "IN", RefEntity: "RECEIPT", RefId: 3),
                 })
        {
            var (query, _) = await reads.BuildKardexQueryAsync(q, InventoryScope.Any, default);
            var list = query.OrderByDescending(t => t.CreatedAtUtc).Skip(0).Take(50).ToQueryString();
            Assert.Contains("FROM [InventoryTransaction]", list);
            var groups = InventoryReadService.SummaryGroupsQuery(query).ToQueryString();
            Assert.Contains("GROUP BY", groups);
            Assert.Contains("SUM(", groups);
        }
        var (manual, _) = await reads.BuildKardexQueryAsync(new KardexQuery(ManualOnly: true, IncludeOwn: true), InventoryScope.Any, default);
        var sql = manual.ToQueryString();
        Assert.Contains("[RefEntityLookupId] IS NULL", sql);
        Assert.Contains("[ClientId] IS NULL", sql);
    }
}
