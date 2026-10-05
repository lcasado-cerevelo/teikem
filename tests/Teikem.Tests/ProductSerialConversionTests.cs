using System.Text.RegularExpressions;
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
/// Lote 26 (Rentas R0) — "Convertir a serie" (ProductSerialConversionService) en InMemory con el InventoryLedger REAL (WmsFixture):
/// por posición, ADJUSTMENT − del saldo sin serie y ADJUSTMENT + por serie con el motivo de sistema TRACKING_CONVERSION, y el
/// seguimiento NONE → SERIAL; el en mano no cambia y las series nacen AVAILABLE en su posición. Las negativas (400 por conteo,
/// 409 por reservas o documentos abiertos, 422 por seguimiento o existencia no convertible, 403 sin inventory.adjust, 404 de otra
/// compañía) no escriben nada. D25 sigue bloqueando el PATCH. La única escritura de TrackingTypeLookupId fuera de ProductService es
/// la de este servicio (prueba por texto). El bloqueo real de SQL Server y la reversa de la transacción los cubre el smoke.
/// </summary>
public sealed class ProductSerialConversionTests
{
    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin P1, WarehouseBin P2, WarehouseBin P3, Product Pn);

    private static async Task<World> SeedAsync(decimal qtyP1 = 2m, decimal qtyP2 = 1m)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<Teikem.Infrastructure.Abstractions.ITenantClock>(Teikem.Infrastructure.Abstractions.TenantClock.Default);
            s.AddSingleton<InventoryReconciler>();
            s.AddSingleton<InventoryReadService>();
            s.AddSingleton<ProductService>();
            s.AddSingleton<ProductSerialConversionService>();
        });
        var w = await f.AddWarehouseAsync("W1");
        var zone = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var p1 = await f.AddBinAsync(zone, "P-01");
        var p2 = await f.AddBinAsync(zone, "P-02");
        var p3 = await f.AddBinAsync(zone, "P-03");
        var pn = await f.AddProductAsync("EQ-1");
        var postings = new List<InventoryPosting>();
        if (qtyP1 > 0) postings.Add(new InventoryPosting(InventoryTxnTypes.Adjustment, pn.ProductId, qtyP1, ToWarehouseId: w.WarehouseId, ToBinId: p1.WarehouseBinId,
            ReasonCode: AdjustmentReasons.Found, Notes: "saldo"));
        if (qtyP2 > 0) postings.Add(new InventoryPosting(InventoryTxnTypes.Adjustment, pn.ProductId, qtyP2, ToWarehouseId: w.WarehouseId, ToBinId: p2.WarehouseBinId,
            ReasonCode: AdjustmentReasons.Found, Notes: "saldo"));
        if (postings.Count > 0) await f.PostAsync(postings.ToArray());
        return new World(f, w, p1, p2, p3, pn);
    }

    private static SerialConversionPositionInput At(WarehouseBin bin, params string[] serials) => new(bin.WarehouseBinId, serials);

    private static async Task<ProductSerialConversionResultDto> ConvertAsync(World w, params SerialConversionPositionInput[] positions)
    {
        try { return await w.F.Get<ProductSerialConversionService>().ConvertAsync(w.Pn.PublicId, new ProductSerialConversionRequest(positions), default); }
        finally { w.F.Db.ChangeTracker.Clear(); }
    }

    private static async Task<string> TrackingAsync(World w)
    {
        w.F.Db.ChangeTracker.Clear();
        var id = await w.F.Db.Products.AsNoTracking().Where(p => p.ProductId == w.Pn.ProductId).Select(p => p.TrackingTypeLookupId).SingleAsync();
        return new[] { TrackingTypes.None, TrackingTypes.Lot, TrackingTypes.Serial }.Single(c => w.F.LookupId(LookupDomains.TrackingType, c) == id);
    }

    /// <summary>Nada se escribió: seguimiento NONE, mismos movimientos y ninguna serie.</summary>
    private static async Task AssertUntouchedAsync(World w, int txnsBefore)
    {
        Assert.Equal(TrackingTypes.None, await TrackingAsync(w));
        Assert.Equal(txnsBefore, (await w.F.TransactionsAsync()).Count);
        Assert.False(await w.F.Db.InventorySerials.AsNoTracking().AnyAsync(s => s.ProductId == w.Pn.ProductId));
    }

    [Fact]
    public async Task Converts_each_position_through_the_ledger_and_switches_tracking_to_serial()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var before = (await w.F.TransactionsAsync()).Count;

        var result = await ConvertAsync(w, At(w.P1, "SN-1", "SN-2"), At(w.P2, "SN-3"));

        Assert.Equal(TrackingTypes.Serial, await TrackingAsync(w));
        Assert.Equal(TrackingTypes.Serial, result.Product.Product.TrackingTypeCode);
        Assert.True(result.Product.HasMovements);
        Assert.Equal(3, result.SerialCount);
        // El en mano de cada posición no cambia (sale el saldo sin serie y entra una unidad por serie).
        Assert.Equal(2m, await w.F.OnHandAsync(w.Pn.ProductId, w.P1.WarehouseBinId));
        Assert.Equal(1m, await w.F.OnHandAsync(w.Pn.ProductId, w.P2.WarehouseBinId));
        Assert.Equal(new[] { 1m, 2m }, result.Movements.Balances.Select(b => b.QtyOnHand).OrderBy(q => q));

        // Movimientos: por posición un − del saldo sin serie y un + por serie, todos con el motivo de sistema y la nota por defecto.
        var txns = (await w.F.TransactionsAsync()).Skip(before).ToList();
        Assert.Equal(5, txns.Count);
        var reasonId = w.F.LookupId(LookupDomains.AdjustmentReason, AdjustmentReasons.TrackingConversion);
        var adjustmentId = w.F.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Adjustment);
        Assert.All(txns, t =>
        {
            Assert.Equal(reasonId, t.ReasonLookupId);
            Assert.Equal(adjustmentId, t.TxnTypeLookupId);
            Assert.Equal(SerialConversionRules.DefaultNotes, t.Notes);
            Assert.Null(t.RefEntityLookupId);
        });
        Assert.Equal(new[] { -2m, 1m, 1m, -1m, 1m }, txns.Select(t => t.Quantity));
        Assert.Equal(new int?[] { null, w.P1.WarehouseBinId, w.P1.WarehouseBinId, null, w.P2.WarehouseBinId }, txns.Select(t => t.ToBinId));
        Assert.All(txns.Where(t => t.Quantity < 0), t => Assert.Null(t.SerialId));
        Assert.All(txns.Where(t => t.Quantity > 0), t => Assert.NotNull(t.SerialId));
        Assert.Equal(5, result.Movements.Transactions.Count);
        Assert.All(result.Movements.Transactions, r => Assert.Equal(AdjustmentReasons.TrackingConversion, r.ReasonCode));

        // Las series nacen AVAILABLE en su posición, con historial null → AVAILABLE.
        foreach (var (number, bin) in new[] { ("SN-1", w.P1), ("SN-2", w.P1), ("SN-3", w.P2) })
        {
            var serial = await w.F.SerialAsync(w.Pn.ProductId, number);
            Assert.Equal(w.F.StatusId(StatusDomains.SerialStatus, SerialStatuses.Available), serial.StatusCodeId);
            Assert.Equal(w.W.WarehouseId, serial.CurrentWarehouseId);
            Assert.Equal(bin.WarehouseBinId, serial.CurrentBinId);
            Assert.Equal(new[] { SerialStatuses.Available }, await w.F.HistoryCodesAsync(EntityTypes.InventorySerial, serial.SerialId));
        }

        // D25 sigue en pie: con movimientos, el seguimiento no se cambia por PATCH (ni de vuelta a NONE).
        var patch = await Assert.ThrowsAsync<ConflictException>(() =>
            w.F.Get<ProductService>().UpdateAsync(w.Pn.PublicId, new ProductPatchRequest(TrackingType: TrackingTypes.None), default));
        Assert.Equal(ProductRules.Immutable(ProductRules.FieldTracking), patch.Message);
        w.F.Db.ChangeTracker.Clear();
        // Ya es SERIAL: una segunda conversión → 422.
        var again = await Assert.ThrowsAsync<StatusRuleException>(() => ConvertAsync(w));
        Assert.Equal(SerialConversionRules.AlreadySerial("EQ-1"), again.Message);
    }

    [Fact]
    public async Task Notes_are_kept_and_a_product_without_stock_only_switches_tracking()
    {
        var w = await SeedAsync(qtyP1: 1m, qtyP2: 0m);
        await using var _ = w.F;
        // Sale lo que había: el producto tiene movimientos pero no existencia.
        await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment, w.Pn.ProductId, 1m, FromWarehouseId: w.W.WarehouseId,
            FromBinId: w.P1.WarehouseBinId, ReasonCode: AdjustmentReasons.Loss, Notes: "salida"));
        var before = (await w.F.TransactionsAsync()).Count;

        var result = await w.F.Get<ProductSerialConversionService>().ConvertAsync(w.Pn.PublicId,
            new ProductSerialConversionRequest(Array.Empty<SerialConversionPositionInput>(), "  Equipos de renta  "), default);

        Assert.Equal(TrackingTypes.Serial, await TrackingAsync(w));
        Assert.Equal(0, result.SerialCount);
        Assert.Empty(result.Movements.Transactions);
        Assert.Equal(before, (await w.F.TransactionsAsync()).Count);

        // Con existencia, la nota capturada (recortada) va en cada movimiento.
        var w2 = await SeedAsync(qtyP1: 1m, qtyP2: 0m);
        await using var __ = w2.F;
        var r2 = await w2.F.Get<ProductSerialConversionService>().ConvertAsync(w2.Pn.PublicId,
            new ProductSerialConversionRequest(new[] { At(w2.P1, "A-1") }, "  Equipos de renta  "), default);
        Assert.All(r2.Movements.Transactions, t => Assert.Equal("Equipos de renta", t.Notes));
    }

    [Fact]
    public async Task Serial_count_must_match_the_units_of_each_position()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var before = (await w.F.TransactionsAsync()).Count;

        // P-01 tiene 2 y se capturó 1.
        var fewer = await Assert.ThrowsAsync<ValidationException>(() => ConvertAsync(w, At(w.P1, "SN-1"), At(w.P2, "SN-3")));
        Assert.Equal("Capture 2 número(s) de serie para P-01 (hay 1).", fewer.Message);
        // P-02 tiene existencia y no vino en la solicitud.
        var missing = await Assert.ThrowsAsync<ValidationException>(() => ConvertAsync(w, At(w.P1, "SN-1", "SN-2")));
        Assert.Equal("Capture 1 número(s) de serie para P-02 (hay 0).", missing.Message);
        // P-03 no tiene existencia y trae series.
        var extra = await Assert.ThrowsAsync<ValidationException>(() => ConvertAsync(w, At(w.P1, "SN-1", "SN-2"), At(w.P2, "SN-3"), At(w.P3, "SN-4")));
        Assert.Equal("Capture 0 número(s) de serie para P-03 (hay 1).", extra.Message);
        // La misma serie en dos posiciones → 400 repetida; posición sin id → 400.
        var dup = await Assert.ThrowsAsync<ValidationException>(() => ConvertAsync(w, At(w.P1, "SN-1", "SN-2"), At(w.P2, "sn-1")));
        Assert.Equal(AdjustmentRules.SerialDuplicated("sn-1"), Assert.Single(dup.Errors!).Value[0]);
        var noBin = await Assert.ThrowsAsync<ValidationException>(() => ConvertAsync(w, new SerialConversionPositionInput(null, new[] { "X" })));
        Assert.Equal(AdjustmentRules.BinRequired, noBin.Errors!["positions[0].binId"][0]);

        await AssertUntouchedAsync(w, before);

        // El mismo renglón partido en dos se suma.
        var ok = await ConvertAsync(w, At(w.P1, "SN-1"), At(w.P2, "SN-3"), At(w.P1, "SN-2"));
        Assert.Equal(3, ok.SerialCount);
    }

    [Fact]
    public async Task Reserved_units_or_open_documents_block_the_conversion()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await w.F.ReserveAsync(new StockReservation(w.Pn.ProductId, w.W.WarehouseId, w.P1.WarehouseBinId, null, 1m));
        var before = (await w.F.TransactionsAsync()).Count;

        var reserved = await Assert.ThrowsAsync<ConflictException>(() => ConvertAsync(w, At(w.P1, "SN-1", "SN-2"), At(w.P2, "SN-3")));
        Assert.Equal("El producto EQ-1 tiene unidades reservadas; libérelas antes de convertirlo.", reserved.Message);
        await AssertUntouchedAsync(w, before);

        // Liberada la reserva, una tarea pendiente del producto (acomodo) también bloquea.
        await w.F.ReleaseAsync(new StockReservation(w.Pn.ProductId, w.W.WarehouseId, w.P1.WarehouseBinId, null, 1m));
        await w.F.AddTaskAsync(new WarehouseTaskSpec(WarehouseTaskTypes.Putaway, w.W.WarehouseId, w.Pn.ProductId, 1m,
            FromBinId: w.P2.WarehouseBinId, ToBinId: w.P3.WarehouseBinId));
        var open = await Assert.ThrowsAsync<ConflictException>(() => ConvertAsync(w, At(w.P1, "SN-1", "SN-2"), At(w.P2, "SN-3")));
        Assert.Equal(SerialConversionRules.OpenDocuments("EQ-1"), open.Message);
        await AssertUntouchedAsync(w, before);
    }

    [Fact]
    public async Task Only_untracked_products_with_whole_units_in_bins_are_converted()
    {
        var w = await SeedAsync(qtyP1: 0m, qtyP2: 0m);
        await using var _ = w.F;
        var svc = w.F.Get<ProductSerialConversionService>();

        var lot = await w.F.AddProductAsync("LT-1", TrackingTypes.Lot);
        var lotError = await Assert.ThrowsAsync<StatusRuleException>(() => svc.ConvertAsync(lot.PublicId, new ProductSerialConversionRequest(), default));
        Assert.Equal("Solo se convierten a serie productos sin seguimiento; LT-1 se controla por lote.", lotError.Message);
        w.F.Db.ChangeTracker.Clear();
        var serial = await w.F.AddProductAsync("SR-1", TrackingTypes.Serial);
        var serialError = await Assert.ThrowsAsync<StatusRuleException>(() => svc.ConvertAsync(serial.PublicId, new ProductSerialConversionRequest(), default));
        Assert.Equal("El producto SR-1 ya se controla por serie.", serialError.Message);
        w.F.Db.ChangeTracker.Clear();

        // Existencia fraccionaria: no hay una serie por unidad.
        await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment, w.Pn.ProductId, 1.5m, ToWarehouseId: w.W.WarehouseId,
            ToBinId: w.P1.WarehouseBinId, ReasonCode: AdjustmentReasons.Found, Notes: "fracción"));
        var before = (await w.F.TransactionsAsync()).Count;
        var fraction = await Assert.ThrowsAsync<StatusRuleException>(() => ConvertAsync(w, At(w.P1, "SN-1")));
        Assert.Equal("La existencia de EQ-1 en P-01 es 1.5; ajústela a unidades enteras antes de convertirlo.", fraction.Message);
        await AssertUntouchedAsync(w, before);
    }

    [Fact]
    public async Task Requires_inventory_adjust_in_the_service_and_isolates_the_company()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var before = (await w.F.TransactionsAsync()).Count;

        // Con inventory.manage (lo que pide el controlador) pero sin inventory.adjust → 403 sin escribir nada.
        w.F.SetPermissions(PermissionCatalog.InventoryView, PermissionCatalog.InventoryManage);
        var forbidden = await Assert.ThrowsAsync<ForbiddenException>(() => ConvertAsync(w, At(w.P1, "SN-1", "SN-2"), At(w.P2, "SN-3")));
        Assert.Equal($"Falta el permiso '{PermissionCatalog.InventoryAdjust}'.", forbidden.Message);
        await AssertUntouchedAsync(w, before);

        // Otra compañía: el producto no existe para ella (404) y una posición ajena tampoco.
        w.F.SetPermissions(PermissionCatalog.InventoryManage, PermissionCatalog.InventoryAdjust);
        using (w.F.AsTenant(WmsFixture.OtherTenantId))
        {
            w.F.SetPermissions(PermissionCatalog.InventoryManage, PermissionCatalog.InventoryAdjust);   // permisos de la sesión en esa compañía
            var foreign = await Assert.ThrowsAsync<NotFoundException>(() => ConvertAsync(w, At(w.P1, "SN-1", "SN-2"), At(w.P2, "SN-3")));
            Assert.Equal("Producto no encontrado.", foreign.Message);
        }
        var otherWarehouse = await w.F.AddWarehouseAsync("W9", tenantId: WmsFixture.OtherTenantId);
        var otherBin = await w.F.AddBinAsync(await w.F.AddZoneAsync(otherWarehouse, "Z9", ZoneTypes.Picking), "X-01");
        var foreignBin = await Assert.ThrowsAsync<NotFoundException>(() => ConvertAsync(w, At(w.P1, "SN-1", "SN-2"), At(w.P2, "SN-3"), At(otherBin, "SN-9")));
        Assert.Equal("Posición no encontrada.", foreignBin.Message);
        await AssertUntouchedAsync(w, before);

        // Control positivo: con los dos permisos se convierte.
        var ok = await ConvertAsync(w, At(w.P1, "SN-1", "SN-2"), At(w.P2, "SN-3"));
        Assert.Equal(TrackingTypes.Serial, ok.Product.Product.TrackingTypeCode);
    }

    [Fact]
    public void The_reason_is_reserved_to_the_system_and_seeded()
    {
        Assert.Contains(AdjustmentReasons.TrackingConversion, AdjustmentReasons.SystemAssigned);
        var (code, error) = AdjustmentRules.ValidateReason("tracking_conversion", new[] { AdjustmentReasons.TrackingConversion, AdjustmentReasons.Found });
        Assert.Null(code);
        Assert.Equal("El motivo TRACKING_CONVERSION lo asigna el sistema.", error);
        Assert.False(AdjustmentRules.RequiresNotes(AdjustmentReasons.TrackingConversion));
        var seed = File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-seed.sql"));
        Assert.Contains("('AdjustmentReason','TRACKING_CONVERSION','Conversión a serie','Serial tracking conversion',11)", seed, StringComparison.Ordinal);
    }

    [Fact]
    public void Pure_rules_report_the_first_mismatch_by_bin_code_with_the_exact_messages()
    {
        var stock = new[] { new SerialConversionStock(2, "B-02", 3m), new SerialConversionStock(1, "A-01", 1m) };
        var codes = new Dictionary<int, string> { [1] = "A-01", [2] = "B-02", [9] = "Z-09" };
        Assert.Null(SerialConversionRules.FirstCountMismatch(stock, new Dictionary<int, int> { [1] = 1, [2] = 3 }, codes));
        Assert.Equal("Capture 1 número(s) de serie para A-01 (hay 0).",
            SerialConversionRules.FirstCountMismatch(stock, new Dictionary<int, int> { [2] = 2 }, codes));
        Assert.Equal("Capture 3 número(s) de serie para B-02 (hay 2).",
            SerialConversionRules.FirstCountMismatch(stock, new Dictionary<int, int> { [1] = 1, [2] = 2 }, codes));
        Assert.Equal("Capture 0 número(s) de serie para Z-09 (hay 4).",
            SerialConversionRules.FirstCountMismatch(stock, new Dictionary<int, int> { [1] = 1, [2] = 3, [9] = 4 }, codes));
        Assert.Null(SerialConversionRules.FirstCountMismatch(Array.Empty<SerialConversionStock>(), new Dictionary<int, int> { [9] = 0 }, codes));

        Assert.Null(SerialConversionRules.CheckSourceTracking(TrackingTypes.None, "X"));
        Assert.Null(SerialConversionRules.CheckSourceTracking(null, "X"));
        Assert.Equal("El producto X ya se controla por serie.", SerialConversionRules.CheckSourceTracking("serial", "X"));
        Assert.Equal("Solo se convierten a serie productos sin seguimiento; X se controla por lote.", SerialConversionRules.CheckSourceTracking(TrackingTypes.Lot, "X"));
        Assert.True(SerialConversionRules.IsWholeUnits(3m));
        Assert.True(SerialConversionRules.IsWholeUnits(3.000m));
        Assert.False(SerialConversionRules.IsWholeUnits(0.5m));
        Assert.Equal("El producto X tiene unidades reservadas; libérelas antes de convertirlo.", SerialConversionRules.ReservedUnits("X"));
    }

    [Fact]
    public void Tracking_is_written_only_by_the_product_service_and_the_conversion()
    {
        // D25: el seguimiento se fija en el alta y en la edición sin movimientos (ProductService); con movimientos, solo la
        // conversión a serie (ProductSerialConversionService) lo cambia, por el ledger y en la misma transacción.
        var src = Path.Combine(TripCatalogTests.RepoRoot(), "src");
        var pattern = new Regex(@"\bTrackingTypeLookupId\s*=(?![=>])");
        var writers = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => pattern.IsMatch(Regex.Replace(File.ReadAllText(f), @"//[^\n]*", string.Empty)))
            .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "ProductSerialConversionService.cs", "ProductService.cs" }, writers);
    }
}
