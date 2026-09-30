using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 16 — almacén: modo de recepción en el alta y la edición (falta → PUTAWAY; desconocido → 400; null en el PATCH = sin
/// cambio; almacén dado de baja → 422), posición de recepción por defecto (D12: del almacén 404, STAGING/CROSSDOCK 400,
/// activa 422, quitarla) y que la recepción la use primero (H2); fuente de datos con el modo.
/// </summary>
public sealed class WarehouseReceivingModeTests
{
    private static Task<WmsFixture> FixtureAsync()
        => WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<WarehouseLayoutService>();
            s.AddSingleton<WarehouseService>();
            s.AddSingleton<Teikem.Infrastructure.Wms.IPurchaseOrderReceiving>(new ReceivingFakePurchaseOrders());
            s.AddSingleton<AsnService>();
            s.AddSingleton<ReceiptService>();
        });

    /// <summary>Posición de recepción que toma la línea de un recibo ciego nuevo sin posición indicada (la por defecto del almacén).</summary>
    private static async Task<int?> DefaultStagingOfNewReceiptAsync(WmsFixture f, Warehouse w, Product p)
    {
        var created = await f.Get<ReceiptService>().CreateAsync(new ReceiptCreateRequest(w.PublicId, ReceiptTypes.Blind,
            Lines: new[] { new ReceiptLineRequest(p.PublicId, 1m) }, ReceivingMode: ReceivingModes.Putaway), default);
        return Assert.Single(created.Lines).StagingBinId;
    }

    /// <summary>
    /// Alta por el servicio. InMemory no aplica el DEFAULT NEWID() de PublicId: se le da uno propio después del alta para que
    /// el siguiente almacén no choque con Guid.Empty (en SQL Server lo pone la base).
    /// </summary>
    private static async Task<WarehouseDetailDto> CreateAsync(WmsFixture f, WarehouseCreateRequest req)
    {
        var created = await f.Get<WarehouseService>().CreateAsync(req, default);
        var row = await f.Db.Warehouses.SingleAsync(w => w.WarehouseId == created.Warehouse.Id);
        row.PublicId = Guid.NewGuid();
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return await f.Get<WarehouseService>().GetAsync(row.PublicId, default);
    }

    [Fact]
    public async Task Create_and_patch_the_receiving_mode()
    {
        await using var f = await FixtureAsync();
        var service = f.Get<WarehouseService>();

        var plain = await CreateAsync(f, new WarehouseCreateRequest("W1", "Uno"));
        Assert.Equal(ReceivingModes.Putaway, plain.Warehouse.ReceivingModeCode);
        Assert.Equal(f.LookupId(LookupDomains.ReceivingMode, ReceivingModes.Putaway),
            (await f.Db.Warehouses.AsNoTracking().SingleAsync(w => w.WarehouseId == plain.Warehouse.Id)).ReceivingModeLookupId);

        var direct = await CreateAsync(f, new WarehouseCreateRequest("W2", "Dos", ReceivingMode: "direct"));
        Assert.Equal(ReceivingModes.Direct, direct.Warehouse.ReceivingModeCode);
        Assert.Equal(ReceivingModes.Direct, direct.Warehouse.ReceivingMode);   // etiqueta del catálogo del fixture (su código)

        var bad = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(new WarehouseCreateRequest("W3", "Tres", ReceivingMode: "HALF"), default));
        Assert.Equal(new[] { ReceivingModeRules.UnknownMode("HALF") }, bad.Errors!["receivingMode"]);

        var patched = await service.UpdateAsync(plain.Warehouse.PublicId, new WarehousePatchRequest(ReceivingMode: ReceivingModes.Direct), default);
        Assert.Equal(ReceivingModes.Direct, patched.Warehouse.ReceivingModeCode);
        var unchanged = await service.UpdateAsync(plain.Warehouse.PublicId, new WarehousePatchRequest(Name: "Uno bis"), default);
        Assert.Equal(ReceivingModes.Direct, unchanged.Warehouse.ReceivingModeCode);   // null = sin cambio
        var badPatch = await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(plain.Warehouse.PublicId, new WarehousePatchRequest(ReceivingMode: "X"), default));
        Assert.Equal(new[] { ReceivingModeRules.UnknownMode("X") }, badPatch.Errors!["receivingMode"]);

        // Almacén dado de baja → 422.
        var inactive = await f.AddWarehouseAsync("W9", isActive: false);
        await Assert.ThrowsAsync<StatusRuleException>(() => service.UpdateAsync(inactive.PublicId, new WarehousePatchRequest(ReceivingMode: ReceivingModes.Direct), default));

        // Fuente de datos WAREHOUSE: modo y código.
        var rows = await new WarehouseDataSource(f.Db, f.Tenant).LoadAsync(new DataQuery(), default);
        Assert.Contains(rows, r => (string?)r["Code"] == "W2" && (string?)r["ReceivingModeCode"] == ReceivingModes.Direct);
        Assert.Contains(rows, r => (string?)r["Code"] == "W9" && (string?)r["ReceivingModeCode"] == ReceivingModes.Putaway);   // NULL = PUTAWAY
    }

    [Fact]
    public async Task Default_receiving_bin_is_validated_and_used_first_by_receiving()
    {
        await using var f = await FixtureAsync();
        var service = f.Get<WarehouseService>();
        var w = await f.AddWarehouseAsync("W1");
        var other = await f.AddWarehouseAsync("W2");
        // Dos zonas STAGING: SHP (Embarque) < STG por código; sin posición por defecto se elige S1 (el caso de Depot, H2).
        var s1 = await f.AddBinAsync(await f.AddZoneAsync(w, "SHP", ZoneTypes.Staging), "S1");
        var r1 = await f.AddBinAsync(await f.AddZoneAsync(w, "STG", ZoneTypes.Staging), "R1");
        var rsv = await f.AddBinAsync(await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve), "R-01");
        var off = await f.AddBinAsync(await f.AddZoneAsync(w, "ST2", ZoneTypes.Staging), "S9", isActive: false);
        var foreign = await f.AddBinAsync(await f.AddZoneAsync(other, "STG", ZoneTypes.Staging), "X1");
        var p = await f.AddProductAsync("PN");

        Assert.Equal(s1.WarehouseBinId, await DefaultStagingOfNewReceiptAsync(f, w, p));

        var set = await service.UpdateAsync(w.PublicId, new WarehousePatchRequest(DefaultReceivingBinId: r1.WarehouseBinId), default);
        Assert.Equal((r1.WarehouseBinId, "R1"), (set.Warehouse.DefaultReceivingBinId!.Value, set.Warehouse.DefaultReceivingBinCode!));
        Assert.Equal(r1.WarehouseBinId, await DefaultStagingOfNewReceiptAsync(f, w, p));

        var notStaging = await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(w.PublicId, new WarehousePatchRequest(DefaultReceivingBinId: rsv.WarehouseBinId), default));
        Assert.Equal(new[] { ReceiptRules.StagingMustBeStaging }, notStaging.Errors!["defaultReceivingBinId"]);
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(w.PublicId, new WarehousePatchRequest(DefaultReceivingBinId: foreign.WarehouseBinId), default));
        var inactive = await Assert.ThrowsAsync<StatusRuleException>(() => service.UpdateAsync(w.PublicId, new WarehousePatchRequest(DefaultReceivingBinId: off.WarehouseBinId), default));
        Assert.Equal(ReceiptRules.StagingBinInactive, inactive.Message);

        // Si la posición por defecto se desactiva, se vuelve a la regla de siempre (primera STAGING por código).
        (await f.Db.WarehouseBins.SingleAsync(b => b.WarehouseBinId == r1.WarehouseBinId)).IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.Equal(s1.WarehouseBinId, await DefaultStagingOfNewReceiptAsync(f, w, p));

        var cleared = await service.UpdateAsync(w.PublicId, new WarehousePatchRequest(ClearDefaultReceivingBin: true), default);
        Assert.Null(cleared.Warehouse.DefaultReceivingBinId);
        Assert.Null(cleared.Warehouse.DefaultReceivingBinCode);

        // Sin ninguna STAGING: con acomodo responde 422; directo abre sin posición de recepción.
        var bare = await f.AddWarehouseAsync("W3");
        var none = await Assert.ThrowsAsync<StatusRuleException>(() => DefaultStagingOfNewReceiptAsync(f, bare, p));
        Assert.Equal(ReceiptRules.NoStagingBin, none.Message);
        var direct = await f.Get<ReceiptService>().CreateAsync(new ReceiptCreateRequest(bare.PublicId, ReceiptTypes.Blind,
            Lines: new[] { new ReceiptLineRequest(p.PublicId, 1m) }, ReceivingMode: ReceivingModes.Direct), default);
        Assert.Null(Assert.Single(direct.Lines).StagingBinId);
    }
}
