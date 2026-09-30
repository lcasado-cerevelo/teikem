using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 14 (P4; D2, D3, D4, D7, D8, hallazgos 14, 16, 17) — pruebas de SERVICIO del "Conteo de lo cambiado" y de la lista
/// paginada sobre el fixture InMemory del conteo (ledger, estatus y tareas reales). InMemory ignora las transacciones: el "todo
/// o nada" y la traducción a SQL se verifican contra SQL Server (prueba en vivo y humo).
/// </summary>
public sealed class CycleCountFromChangesTests
{
    private static DateTime Past(int minutes) => DateTime.UtcNow.AddMinutes(-minutes);

    [Fact]
    public async Task Creates_one_count_per_changed_position_with_its_task_origin_and_window()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 5m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 3m);
        var svc = f.Get<CycleCountService>();

        // Ventana por defecto la primera vez: desde las 00:00 de hoy en hora de Puerto Rico hasta ahora.
        var preview = await svc.PreviewChangesAsync(null, default);
        Assert.Null(preview.Problem);
        Assert.Null(preview.LastChangesToUtc);
        Assert.Equal(TenantClockToday(), preview.FromUtc);
        Assert.Equal((2, 2, 0, 0, 2, 200), (preview.Movements, preview.Positions, preview.PositionsWithOpenCount, preview.PositionsInactive, preview.Lines, preview.MaxPositions));
        Assert.Equal("ALM-01", preview.WarehouseCode);

        var result = await svc.CreateFromChangesAsync(null, default);
        Assert.Equal(2, result.Counts.Count);
        Assert.All(result.Counts, c =>
        {
            Assert.Equal(CycleCountStatuses.Open, c.StatusCode);
            Assert.Equal(CycleCountOrigins.Changes, c.OriginCode);
            Assert.Equal(1, c.BinCount);
            Assert.Equal("PCK", c.ZoneCode);
            Assert.NotNull(c.TaskId);
            Assert.Equal(result.Window.FromUtc, c.ChangesFromUtc);
            Assert.Equal(result.Window.ToUtc, c.ChangesToUtc);
        });
        Assert.Equal(new[] { "A01-R01-N1-P01", "A01-R01-N1-P02" }, result.Counts.Select(c => c.BinCode).OrderBy(x => x));
        foreach (var c in result.Counts)
            Assert.Equal(WarehouseTaskStatuses.Pending, await f.CountTaskStatusAsync(c.Id));

        // Repetir: las dos posiciones ya tienen un conteo pendiente → 400 (y la vista previa lo anuncia).
        var again = await svc.PreviewChangesAsync(new CycleCountFromChangesRequest(FromUtc: preview.FromUtc), default);
        Assert.Equal((0, 2), (again.Positions, again.PositionsWithOpenCount));
        Assert.Equal("Las 2 posiciones con cambios ya tienen un conteo pendiente.", again.Problem);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateFromChangesAsync(new CycleCountFromChangesRequest(FromUtc: preview.FromUtc), default));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(new[] { "Las 2 posiciones con cambios ya tienen un conteo pendiente." }, ex.Errors!["filters"]);

        // La siguiente ventana por defecto arranca donde terminó esta.
        var next = await svc.PreviewChangesAsync(null, default);
        Assert.Equal(result.Window.ToUtc, next.LastChangesToUtc);
        Assert.Equal(result.Window.ToUtc, next.FromUtc);
    }

    [Fact]
    public async Task Counts_the_whole_position_and_the_emptied_keys_at_zero()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        // PL (con lote) llegó ANTES de la ventana: no se movió, pero está en la posición y entra al conteo.
        var lotId = await f.AddLotAsync(f.ProductLotId, "L-1");
        await f.ReceiveAsync(f.ProductLotId, f.PickBin1, 2m, lotId);
        await f.ShiftTxnsAsync(TimeSpan.FromHours(-3));
        var since = Past(60);
        // Dentro de la ventana: PN entra y sale por completo de la posición 1 (queda en 0).
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 4m);
        await f.IssueAsync(f.ProductNoneId, f.PickBin1, 4m);
        var svc = f.Get<CycleCountService>();

        var created = await svc.CreateFromChangesAsync(new CycleCountFromChangesRequest(FromUtc: since), default);
        var count = Assert.Single(created.Counts);
        var detail = await svc.GetAsync(count.Id, null, default);
        Assert.Equal(new[] { ("PL", 2m), ("PN", 0m) }, detail.Lines.Select(l => (l.Sku, l.SystemQty!.Value)).OrderBy(x => x.Sku));
        await svc.DeleteAsync(count.Id, default);

        // Sin las vaciadas: solo lo que tiene en mano.
        var only = await svc.CreateFromChangesAsync(new CycleCountFromChangesRequest(FromUtc: since, IncludeEmpty: false), default);
        var onlyDetail = await svc.GetAsync(Assert.Single(only.Counts).Id, null, default);
        Assert.Equal("PL", Assert.Single(onlyDetail.Lines).Sku);
    }

    [Fact]
    public async Task Emptied_position_without_empty_lines_has_nothing_to_count()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 4m);
        await f.IssueAsync(f.ProductNoneId, f.PickBin1, 4m);
        var svc = f.Get<CycleCountService>();

        var preview = await svc.PreviewChangesAsync(new CycleCountFromChangesRequest(IncludeEmpty: false), default);
        Assert.Equal((0, 1), (preview.Positions, preview.PositionsEmpty));
        Assert.Equal(CycleCountRules.NothingSelected, preview.Problem);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateFromChangesAsync(new CycleCountFromChangesRequest(IncludeEmpty: false), default));
        Assert.Equal(CycleCountRules.NothingSelected, ex.Message);

        var withEmpty = await svc.CreateFromChangesAsync(null, default);
        var line = Assert.Single((await svc.GetAsync(Assert.Single(withEmpty.Counts).Id, null, default)).Lines);
        Assert.Equal(0m, line.SystemQty);
    }

    [Fact]
    public async Task Count_adjustments_do_not_generate_new_counts_and_the_confirmed_count_ends_in_variance()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 5m);
        var svc = f.Get<CycleCountService>();
        var first = await svc.CreateFromChangesAsync(null, default);
        var count = Assert.Single(first.Counts);
        var detail = await svc.GetAsync(count.Id, null, default);

        // "Confirmar conteo y ajustar" en un paso desde Pendiente: contado 4 de 5 → ajuste −1 → 'Diferencia'.
        await svc.CaptureAsync(count.Id, new CountCaptureRequest(new[] { new CountCaptureItem(detail.Lines[0].Id, 4m) }), default);
        var done = await svc.ReconcileAsync(count.Id, null, default);
        Assert.Equal(CycleCountStatuses.ReconciledVariance, done.Count.StatusCode);
        Assert.Equal(new[] { CycleCountStatuses.Open, CycleCountStatuses.ReconciledVariance }, await f.HistoryAsync(count.Id));

        // El único movimiento nuevo es el ajuste del conteo (Ref CYCLE_COUNT): no hay nada que contar.
        var preview = await svc.PreviewChangesAsync(null, default);
        Assert.Equal(0, preview.Movements);
        Assert.StartsWith("No hubo movimientos en ALM-01 entre ", preview.Problem);
        Assert.EndsWith("; no hay posiciones que contar.", preview.Problem);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateFromChangesAsync(null, default));
        Assert.Equal(preview.Problem, ex.Message);
    }

    [Fact]
    public async Task Skips_inactive_positions_filters_zones_and_validates_the_window()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        var otherZone = await f.AddZoneWithBinAsync("RSV", 201, "B01-R01-N1-P01");
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 1m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 1m);
        await f.ReceiveAsync(f.ProductNoneId, 201, 1m);
        await f.SetBinActiveAsync(f.PickBin2, false);
        var svc = f.Get<CycleCountService>();

        var all = await svc.PreviewChangesAsync(null, default);
        Assert.Equal((2, 1), (all.Positions, all.PositionsInactive));
        var zoned = await svc.PreviewChangesAsync(new CycleCountFromChangesRequest(ZoneIds: new[] { otherZone }), default);
        Assert.Equal((1, 0), (zoned.Positions, zoned.PositionsInactive));
        var created = await svc.CreateFromChangesAsync(new CycleCountFromChangesRequest(ZoneIds: new[] { otherZone }), default);
        Assert.Equal("B01-R01-N1-P01", Assert.Single(created.Counts).BinCode);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.PreviewChangesAsync(new CycleCountFromChangesRequest(ZoneIds: new[] { 999 }), default));
        var inverted = await Assert.ThrowsAsync<ValidationException>(() => svc.PreviewChangesAsync(new CycleCountFromChangesRequest(FromUtc: Past(10), ToUtc: Past(20)), default));
        Assert.Equal(new[] { "La fecha 'desde' no puede ser posterior a la fecha 'hasta'." }, inverted.Errors!["fromUtc"]);
        var tooLong = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateFromChangesAsync(new CycleCountFromChangesRequest(FromUtc: DateTime.UtcNow.AddDays(-40)), default));
        Assert.Equal(new[] { "El rango de \"lo cambiado\" admite como máximo 31 días." }, tooLong.Errors!["fromUtc"]);
    }

    [Fact]
    public async Task More_than_200_positions_is_400_and_creates_nothing()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        var bins = await f.AddBinsAsync(201);
        await f.PostManyAsync(bins.Select(b => new InventoryPosting(InventoryTxnTypes.Receipt, f.ProductNoneId, 1m, ToWarehouseId: f.WarehouseId, ToBinId: b)).ToList());
        var svc = f.Get<CycleCountService>();

        var preview = await svc.PreviewChangesAsync(null, default);
        Assert.Equal(201, preview.Positions);
        var msg = "Hay 201 posiciones con cambios; se generan como máximo 200 a la vez. Acote el rango de fechas o las zonas.";
        Assert.Equal(msg, preview.Problem);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateFromChangesAsync(null, default));
        Assert.Equal(msg, ex.Message);
        Assert.Equal(0, await f.Db.Set<CycleCount>().CountAsync());
    }

    // ================================================================ lista paginada (hallazgo 14) y campos nuevos

    [Fact]
    public async Task Paged_list_gives_total_origin_zone_bin_assignee_and_barcode()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.SetBarcodeAsync(f.ProductNoneId, "7501234567890");
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 2m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 2m);
        var svc = f.Get<CycleCountService>();
        var changes = await svc.CreateFromChangesAsync(null, default);              // dos conteos, uno por posición
        var manual2 = await svc.CreateAsync(new CycleCountCreateRequest(), default);   // uno de dos posiciones, por selección
        await f.AssignCountTaskAsync(changes.Counts[0].Id, 77, "Ana Contadora");

        var page = await svc.ListPageAsync(new CycleCountQuery(Take: 1), blind: false, default);
        Assert.Equal((3, 0, 1), (page.Total, page.Skip, page.Take));
        Assert.Single(page.Items);

        var byOrigin = await svc.ListPageAsync(new CycleCountQuery(Origins: new[] { "changes" }), blind: false, default);
        Assert.Equal(changes.Counts.Count, byOrigin.Total);
        var assigned = byOrigin.Items.Single(c => c.Id == changes.Counts[0].Id);
        Assert.Equal(("Ana Contadora", 77), (assigned.AssignedToName, assigned.AssignedToUserId));
        Assert.Equal(CycleCountOrigins.Changes, assigned.OriginCode);

        var manualRow = Assert.Single((await svc.ListPageAsync(new CycleCountQuery(Origins: new[] { CycleCountOrigins.Manual }), blind: false, default)).Items);
        Assert.Equal((manual2.Count.Id, 2, (string?)null, CycleCountOrigins.Manual), (manualRow.Id, manualRow.BinCount, manualRow.BinCode, manualRow.OriginCode));

        // Zona: todas las líneas están en PCK; otra zona no trae nada. Estatus: los pendientes.
        Assert.Equal(3, (await svc.ListPageAsync(new CycleCountQuery(ZoneIds: new[] { 11 }), blind: false, default)).Total);
        Assert.Equal(0, (await svc.ListPageAsync(new CycleCountQuery(ZoneIds: new[] { 999 }), blind: false, default)).Total);
        Assert.Equal(3, (await svc.ListPageAsync(new CycleCountQuery(Status: new[] { CycleCountStatuses.Open }, Take: 1), blind: false, default)).Total);

        // A ciegas: sin diferencia en el encabezado; el código de barras de la línea sí llega (no revela lo esperado).
        var blind = await svc.ListPageAsync(new CycleCountQuery(), blind: true, default);
        Assert.All(blind.Items, c => Assert.Null(c.VarianceLines));
        var detail = await svc.GetAsync(manual2.Count.Id, null, blind: true, default);
        Assert.Contains(detail.Lines, l => l.Sku == "PN" && l.Barcode == "7501234567890");
        Assert.Equal(CycleCountOrigins.Manual, detail.Count.OriginCode);
    }

    private static DateTime TenantClockToday()
        => Teikem.Domain.Common.LocalDay.StartOfDayUtc(Teikem.Infrastructure.Abstractions.TenantClock.Default.Today, Teikem.Domain.Common.LocalDay.DefaultZone);
}
