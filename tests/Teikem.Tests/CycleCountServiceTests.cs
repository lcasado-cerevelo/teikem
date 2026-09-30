using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Tenancy;
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
/// Lote 6 (P6) — pruebas de SERVICIO del conteo cíclico (D22) sobre InMemory, con StatusService, InventoryLedger y
/// WarehouseTaskWriter reales (resueltos por DI). El inventario se siembra y se mueve SOLO por el ledger. Con InMemory los
/// bloqueos de InventoryQueries cargan tracked sin bloqueo: se prueba la orquestación (ajuste contra el saldo ACTUAL,
/// SystemQtyChanged, guarda de reservado sin efectos, doble reconciliación, tarea COUNT), no la concurrencia (smoke).
/// </summary>
public sealed class CycleCountServiceTests
{
    [Fact]
    public async Task Reconcile_adjusts_against_current_balance_and_flags_moved_snapshot()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 5m);
        var svc = f.Get<CycleCountService>();

        var created = await svc.CreateAsync(new CycleCountCreateRequest(), default);
        Assert.Equal("CC-00001", created.Count.Number);
        Assert.Equal(CycleCountStatuses.Open, created.Count.StatusCode);
        Assert.Equal(2, created.Lines.Count);
        var moved = created.Lines.Single(l => l.BinId == f.PickBin1);
        var still = created.Lines.Single(l => l.BinId == f.PickBin2);
        Assert.Equal(8m, moved.SystemQty);
        Assert.False(moved.IsStale);

        // Foto 8 → se cuentan 9; la otra línea: foto 5 → se cuentan 4.
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[]
        {
            new CountCaptureItem(moved.Id, 9m), new CountCaptureItem(still.Id, 4m),
        }), default);

        // Despacho de 1 DESPUÉS de la foto: el saldo actual de la primera posición es 7.
        await f.IssueAsync(f.ProductNoneId, f.PickBin1, 1m);
        var informed = await svc.GetAsync(created.Count.Id, null, default);
        var staleLine = informed.Lines.Single(l => l.Id == moved.Id);
        Assert.True(staleLine.IsStale);
        Assert.Equal(7m, staleLine.CurrentQty);
        Assert.Equal(1m, staleLine.VarianceQty); // informativa, contra la foto

        var before = await f.TxnCountAsync();
        var result = await svc.ReconcileAsync(created.Count.Id, new CountReconcileRequest("Conteo semanal"), default);

        // Lote 14 (D7, D8): asentó ajustes → 'Diferencia', en un paso desde Pendiente (sin pasar por Contado).
        Assert.Equal(CycleCountStatuses.ReconciledVariance, result.Count.StatusCode);
        Assert.Equal(new[] { CycleCountStatuses.Open, CycleCountStatuses.ReconciledVariance }, await f.HistoryAsync(created.Count.Id));
        Assert.NotNull(result.Count.ReconciledAtUtc);
        var r1 = result.Lines.Single(l => l.Id == moved.Id);
        Assert.True(r1.SystemQtyChanged);
        Assert.Equal(7m, r1.ReconciledSystemQty);
        Assert.Equal(2m, r1.AdjustedQty);          // contado 9 − saldo actual 7
        Assert.NotNull(r1.AdjustmentTxnId);
        Assert.False(r1.IsStale);
        var r2 = result.Lines.Single(l => l.Id == still.Id);
        Assert.False(r2.SystemQtyChanged);
        Assert.Equal(5m, r2.ReconciledSystemQty);
        Assert.Equal(-1m, r2.AdjustedQty);
        Assert.NotNull(r2.AdjustmentTxnId);

        // Ledger: dos ADJUSTMENT COUNT_VARIANCE con signo y Ref CYCLE_COUNT; saldos = lo contado.
        Assert.Equal(before + 2, await f.TxnCountAsync());
        var t1 = await f.TxnAsync(r1.AdjustmentTxnId!.Value);
        Assert.Equal(2m, t1.Quantity);
        Assert.Equal(f.LookupId(LookupDomains.AdjustmentReason, AdjustmentReasons.CountVariance), t1.ReasonLookupId);
        Assert.Equal(f.LookupId(LookupDomains.EntityType, EntityTypes.CycleCount), t1.RefEntityLookupId);
        Assert.Equal(created.Count.Id, t1.RefId);
        Assert.Equal(f.PickBin1, t1.ToBinId);
        var t2 = await f.TxnAsync(r2.AdjustmentTxnId!.Value);
        Assert.Equal(-1m, t2.Quantity);
        Assert.Equal(f.PickBin2, t2.FromBinId);
        Assert.Equal(9m, await f.OnHandAsync(f.ProductNoneId, f.PickBin1));
        Assert.Equal(4m, await f.OnHandAsync(f.ProductNoneId, f.PickBin2));

        // Tarea COUNT cerrada.
        Assert.Equal(WarehouseTaskStatuses.Done, await f.CountTaskStatusAsync(created.Count.Id));
    }

    [Fact]
    public async Task Serial_count_writes_off_missing_adds_new_and_transfers_misplaced()
    {
        // D22: en serie, las faltantes se dan de baja, las nuevas entran y las de otra posición se transfieren.
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveSerialAsync(f.ProductSerialId, f.PickBin1, "S1");
        await f.ReceiveSerialAsync(f.ProductSerialId, f.PickBin1, "S2");
        await f.ReceiveSerialAsync(f.ProductSerialId, f.PickBin2, "S3");
        var svc = f.Get<CycleCountService>();
        var created = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }, ProductPublicIds: new[] { f.ProductSerialPublicId }), default);
        var line = Assert.Single(created.Lines);
        Assert.Equal(2m, line.SystemQty);
        Assert.Equal(new[] { "S1", "S2" }, line.ExpectedSerials.OrderBy(x => x).ToArray());

        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { new CountCaptureItem(line.Id, null, new[] { "S2", "S3", "S9" }) }), default);
        var before = await f.TxnCountAsync();
        var result = await svc.ReconcileAsync(created.Count.Id, null, default);
        Assert.Equal(CycleCountStatuses.ReconciledVariance, result.Count.StatusCode);   // Lote 14: bajas, altas y transferencias

        var txns =(await f.Db.Set<InventoryTransaction>().AsNoTracking().OrderBy(t => t.InventoryTransactionId).ToListAsync()).Skip(before).ToList();
        Assert.Equal(3, txns.Count);
        var serials = await f.Db.Set<InventorySerial>().AsNoTracking().Where(x => x.ProductId == f.ProductSerialId)
            .ToDictionaryAsync(x => x.SerialId, x => x.SerialNumber);
        var bySerial = txns.ToDictionary(t => serials[t.SerialId!.Value]);
        var adjustment = f.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Adjustment);
        var countVariance = f.LookupId(LookupDomains.AdjustmentReason, AdjustmentReasons.CountVariance);
        Assert.Equal((adjustment, -1m, countVariance, (int?)f.PickBin1), (bySerial["S1"].TxnTypeLookupId, bySerial["S1"].Quantity, bySerial["S1"].ReasonLookupId, bySerial["S1"].FromBinId));
        Assert.Equal((adjustment, 1m, countVariance, (int?)f.PickBin1), (bySerial["S9"].TxnTypeLookupId, bySerial["S9"].Quantity, bySerial["S9"].ReasonLookupId, bySerial["S9"].ToBinId));
        var transfer = bySerial["S3"];
        Assert.Equal(f.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Transfer), transfer.TxnTypeLookupId);
        Assert.Equal(((int?)f.PickBin2, (int?)f.PickBin1), (transfer.FromBinId, transfer.ToBinId));
        Assert.False(bySerial.ContainsKey("S2"));
        Assert.Equal(3m, await f.OnHandAsync(f.ProductSerialId, f.PickBin1));
        Assert.Equal(0m, await f.OnHandAsync(f.ProductSerialId, f.PickBin2));
    }

    [Fact]
    public async Task Serial_line_captured_by_quantity_or_twice_is_400()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveSerialAsync(f.ProductSerialId, f.PickBin1, "S1");
        await f.ReceiveSerialAsync(f.ProductSerialId, f.PickBin2, "S2");
        var svc = f.Get<CycleCountService>();
        var created = await svc.CreateAsync(new CycleCountCreateRequest(ProductPublicIds: new[] { f.ProductSerialPublicId }), default);
        Assert.Equal(2, created.Lines.Count);

        var byQty = await Assert.ThrowsAsync<ValidationException>(() => svc.CaptureAsync(created.Count.Id,
            new CountCaptureRequest(new[] { new CountCaptureItem(created.Lines[0].Id, 2m) }), default));
        Assert.Contains(CycleCountRules.SerialCountedByList, byQty.Errors!.Values.SelectMany(v => v));
        f.Db.ChangeTracker.Clear();

        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[]
        {
            new CountCaptureItem(created.Lines[0].Id, null, new[] { "S1" }), new CountCaptureItem(created.Lines[1].Id, null, new[] { "S1" }),
        }), default);
        var before = await f.TxnCountAsync();
        var twice = await Assert.ThrowsAsync<ValidationException>(() => svc.ReconcileAsync(created.Count.Id, null, default));
        Assert.Equal(CycleCountRules.SerialCountedTwice("S1"), Assert.Single(twice.Errors!["lines"]));
        Assert.Equal(before, await f.TxnCountAsync());
    }

    [Fact]
    public async Task Reconcile_without_differences_posts_nothing_and_closes_the_task()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 3m);
        var svc = f.Get<CycleCountService>();
        var created = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }), default);
        var line = Assert.Single(created.Lines);
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { new CountCaptureItem(line.Id, 3m) }), default);
        await svc.FinishAsync(created.Count.Id, null, default);
        var before = await f.TxnCountAsync();

        var result = await svc.ReconcileAsync(created.Count.Id, null, default);

        // Lote 14 (D7): sin movimientos → 'Concordancia'; terminado a ciegas antes → Pendiente → Contado → Concordancia.
        Assert.Equal(CycleCountStatuses.Reconciled, result.Count.StatusCode);
        Assert.Equal(new[] { CycleCountStatuses.Open, CycleCountStatuses.Counted, CycleCountStatuses.Reconciled }, await f.HistoryAsync(created.Count.Id));
        var r = Assert.Single(result.Lines);
        Assert.False(r.SystemQtyChanged);
        Assert.Equal(0m, r.AdjustedQty);
        Assert.Null(r.AdjustmentTxnId);
        Assert.Equal(before, await f.TxnCountAsync());
        Assert.Equal(WarehouseTaskStatuses.Done, await f.CountTaskStatusAsync(created.Count.Id));
    }

    [Fact]
    public async Task Count_below_reserved_is_409_without_effects()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 5m);
        await f.ReserveAsync(f.ProductNoneId, f.PickBin1, 3m);
        var svc = f.Get<CycleCountService>();
        var created = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }), default);
        var line = Assert.Single(created.Lines);
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { new CountCaptureItem(line.Id, 2m) }), default);
        var before = await f.TxnCountAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.ReconcileAsync(created.Count.Id, null, default));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("El conteo de PN en A01-R01-N1-P01 (2) es menor que lo reservado (3); libere la reserva antes de reconciliar.", ex.Message);
        Assert.NotNull(ex.Errors);
        Assert.Contains("lines[0]", ex.Errors!.Keys);

        // Sin efectos: ni movimientos, ni saldo, ni estatus, ni marca de reconciliación, ni tarea cerrada.
        f.Db.ChangeTracker.Clear();
        Assert.Equal(before, await f.TxnCountAsync());
        Assert.Equal(5m, await f.OnHandAsync(f.ProductNoneId, f.PickBin1));
        var after = await svc.GetAsync(created.Count.Id, null, default);
        Assert.Equal(CycleCountStatuses.Open, after.Count.StatusCode);
        Assert.Null(Assert.Single(after.Lines).ReconciledSystemQty);
        Assert.Equal(WarehouseTaskStatuses.Pending, await f.CountTaskStatusAsync(created.Count.Id));
    }

    [Fact]
    public async Task Second_reconcile_is_422_and_capture_after_reconcile_is_422()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 4m);
        var svc = f.Get<CycleCountService>();
        var created = await svc.CreateAsync(new CycleCountCreateRequest(), default);
        var line = Assert.Single(created.Lines);
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { new CountCaptureItem(line.Id, 6m) }), default);
        await svc.ReconcileAsync(created.Count.Id, null, default);
        var txns = await f.TxnCountAsync();

        var again = await Assert.ThrowsAsync<StatusRuleException>(() => svc.ReconcileAsync(created.Count.Id, null, default));
        Assert.Equal(422, again.StatusCode);
        Assert.Equal("El conteo ya fue reconciliado; solo se consulta.", again.Message);
        Assert.Equal(txns, await f.TxnCountAsync());
        Assert.Equal(6m, await f.OnHandAsync(f.ProductNoneId, f.PickBin1));

        var capture = await Assert.ThrowsAsync<StatusRuleException>(() =>
            svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { new CountCaptureItem(line.Id, 1m) }), default));
        Assert.Equal(CycleCountRules.CountNotOpen, capture.Message);
    }

    [Fact]
    public async Task Reconcile_with_pending_lines_is_422()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 2m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 2m);
        var svc = f.Get<CycleCountService>();
        var created = await svc.CreateAsync(new CycleCountCreateRequest(), default);
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { new CountCaptureItem(created.Lines[0].Id, 2m) }), default);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => svc.ReconcileAsync(created.Count.Id, null, default));
        Assert.Equal("Faltan 1 línea(s) por contar.", ex.Message);
        var finish = await Assert.ThrowsAsync<StatusRuleException>(() => svc.FinishAsync(created.Count.Id, null, default));
        Assert.Equal("Faltan 1 línea(s) por contar.", finish.Message);
    }

    [Fact]
    public async Task Line_of_another_count_is_404()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 2m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 2m);
        var svc = f.Get<CycleCountService>();
        var a = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }), default);
        var b = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin2 }), default);
        Assert.Equal("CC-00002", b.Count.Number);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.CaptureAsync(a.Count.Id, new CountCaptureRequest(new[] { new CountCaptureItem(b.Lines[0].Id, 1m) }), default));
        Assert.Equal("Línea del conteo no encontrada.", ex.Message);
    }

    [Fact]
    public async Task Added_line_counts_found_stock_and_duplicate_is_409()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 2m);
        var svc = f.Get<CycleCountService>();
        var created = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }), default);

        // Encontrado: PN en la segunda posición (sin saldo) con 3 unidades.
        var withFound = await svc.AddLineAsync(created.Count.Id, new CountAddLineRequest(f.PickBin2, f.ProductNonePublicId, CountedQty: 3m), default);
        var found = withFound.Lines.Single(l => l.BinId == f.PickBin2);
        Assert.Equal(0m, found.SystemQty);
        Assert.Equal(3m, found.CountedQty);

        var dup = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.AddLineAsync(created.Count.Id, new CountAddLineRequest(f.PickBin2, f.ProductNonePublicId, CountedQty: 1m), default));
        Assert.Equal("Esa posición, producto y lote ya están en el conteo.", dup.Message);

        // Lote obligatorio en productos con lote.
        var lot = await Assert.ThrowsAsync<ValidationException>(() =>
            svc.AddLineAsync(created.Count.Id, new CountAddLineRequest(f.PickBin2, f.ProductLotPublicId, CountedQty: 1m), default));
        Assert.Equal("El producto PL se controla por lote; indique el lote.", lot.Message);

        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { new CountCaptureItem(withFound.Lines.Single(l => l.BinId == f.PickBin1).Id, 2m) }), default);
        var result = await svc.ReconcileAsync(created.Count.Id, null, default);
        Assert.Equal(3m, result.Lines.Single(l => l.BinId == f.PickBin2).AdjustedQty);
        Assert.Equal(3m, await f.OnHandAsync(f.ProductNoneId, f.PickBin2));
    }

    [Fact]
    public async Task Capture_batch_applies_tracking_rules_to_existing_and_found_lines()
    {
        // Lote 8A — PUT /cycle-counts/{id}/lines/batch: mismas reglas que CaptureAsync y AddLineAsync, con errores por renglón
        // ('lines[i].campo') y sin guardar nada si algún renglón falla.
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 2m);
        await f.ReceiveSerialAsync(f.ProductSerialId, f.PickBin1, "S1");
        // Posición inactiva del almacén y producto inactivo (para lo encontrado).
        f.Db.Set<WarehouseBin>().Add(new WarehouseBin { WarehouseBinId = 109, WarehouseZoneId = 11, WarehouseId = f.WarehouseId, Code = "A01-R01-N1-P09", IsActive = false });
        var inactive = new Product
        {
            ProductId = 309, PublicId = Guid.NewGuid(), TenantId = CycleCountFixture.TenantId, Sku = "PX", Name = "Producto PX",
            BaseUomLookupId = f.LookupId(LookupDomains.UnitOfMeasure, "UN"), TrackingTypeLookupId = f.LookupId(LookupDomains.TrackingType, TrackingTypes.None),
            PurchaseCost = 1m, IsActive = false,
        };
        f.Db.Set<Product>().Add(inactive);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var svc = f.Get<CycleCountService>();
        var created = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }), default);
        var none = created.Lines.Single(l => l.Sku == "PN");
        var serial = created.Lines.Single(l => l.Sku == "PS");

        async Task AssertNothingSavedAsync()
        {
            f.Db.ChangeTracker.Clear();
            var lines = await f.Db.Set<CycleCountLine>().AsNoTracking().Where(l => l.CycleCountId == created.Count.Id).ToListAsync();
            Assert.Equal(2, lines.Count);
            Assert.All(lines, l => Assert.Null(l.CountedQty));
        }

        // Serie con cantidad y sin lista → lines[1].countedQty; el renglón válido [0] tampoco se guarda.
        var qty = await Assert.ThrowsAsync<ValidationException>(() => svc.CaptureBatchAsync(created.Count.Id, new CountBatchRequest(new[]
        {
            new CountBatchItem(LineId: none.Id, CountedQty: 2m),
            new CountBatchItem(LineId: serial.Id, CountedQty: 1m),
        }), default));
        Assert.Equal(new[] { CycleCountRules.SerialCountedByList }, qty.Errors!["lines[1].countedQty"]);
        await AssertNothingSavedAsync();

        // Lo encontrado de un producto con lote sin lote → lines[1].lot.
        var lot = await Assert.ThrowsAsync<ValidationException>(() => svc.CaptureBatchAsync(created.Count.Id, new CountBatchRequest(new[]
        {
            new CountBatchItem(LineId: none.Id, CountedQty: 2m),
            new CountBatchItem(BinId: f.PickBin2, ProductPublicId: f.ProductLotPublicId, CountedQty: 1m),
        }), default));
        Assert.Equal(new[] { "El producto PL se controla por lote; indique el lote." }, lot.Errors!["lines[1].lot"]);
        await AssertNothingSavedAsync();

        // Posición inactiva y producto inactivo en lo encontrado → 422.
        var bin = await Assert.ThrowsAsync<StatusRuleException>(() => svc.CaptureBatchAsync(created.Count.Id, new CountBatchRequest(new[]
        {
            new CountBatchItem(BinId: 109, ProductPublicId: f.ProductNonePublicId, CountedQty: 1m),
        }), default));
        Assert.Equal(422, bin.StatusCode);
        Assert.Equal("La posición A01-R01-N1-P09 está inactiva.", bin.Message);
        var product = await Assert.ThrowsAsync<StatusRuleException>(() => svc.CaptureBatchAsync(created.Count.Id, new CountBatchRequest(new[]
        {
            new CountBatchItem(BinId: f.PickBin2, ProductPublicId: inactive.PublicId, CountedQty: 1m),
        }), default));
        Assert.Equal(422, product.StatusCode);
        Assert.Equal(CycleCountRules.ProductInactive("PX"), product.Message);
        await AssertNothingSavedAsync();

        // La misma línea por las dos vías (lineId y posición + producto) → lines[1] repetida; nada se guarda.
        var rep = await Assert.ThrowsAsync<ValidationException>(() => svc.CaptureBatchAsync(created.Count.Id, new CountBatchRequest(new[]
        {
            new CountBatchItem(LineId: none.Id, CountedQty: 1m),
            new CountBatchItem(BinId: f.PickBin1, ProductPublicId: f.ProductNonePublicId, CountedQty: 2m),
        }), default));
        Assert.Equal(new[] { CycleCountService.BatchLineRepeated }, rep.Errors!["lines[1]"]);
        await AssertNothingSavedAsync();

        // Válido: serie por lista, cantidad y lo encontrado de PL con lote por número (se crea el lote y la línea con LotId).
        var result = await svc.CaptureBatchAsync(created.Count.Id, new CountBatchRequest(new[]
        {
            new CountBatchItem(LineId: none.Id, CountedQty: 2m),
            new CountBatchItem(LineId: serial.Id, SerialNumbers: new[] { "S1" }),
            new CountBatchItem(BinId: f.PickBin2, ProductPublicId: f.ProductLotPublicId, Lot: new LotInput("L-8A"), CountedQty: 4m),
        }), default);
        Assert.Equal(3, result.Lines.Count);
        Assert.Equal(2m, result.Lines.Single(l => l.Id == none.Id).CountedQty);
        var s = result.Lines.Single(l => l.Id == serial.Id);
        Assert.Equal(1m, s.CountedQty);
        Assert.Equal(new[] { "S1" }, s.CountedSerials);
        var found = result.Lines.Single(l => l.Sku == "PL");
        Assert.Equal(f.PickBin2, found.BinId);
        Assert.Equal("L-8A", found.LotNumber);
        Assert.NotNull(found.LotId);
        Assert.Equal(0m, found.SystemQty);
        Assert.Equal(4m, found.CountedQty);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(found.LotId, (await f.Db.Set<InventoryLot>().AsNoTracking().SingleAsync(l => l.ProductId == f.ProductLotId && l.LotNumber == "L-8A")).LotId);

        // Como cuenta la app (posición + producto, sin lineId): captura la línea existente y no agrega otra.
        var byKey = await svc.CaptureBatchAsync(created.Count.Id, new CountBatchRequest(new[]
        {
            new CountBatchItem(BinId: f.PickBin1, ProductPublicId: f.ProductNonePublicId, CountedQty: 3m),
        }), default);
        Assert.Equal(3, byKey.Lines.Count);
        Assert.Equal(3m, byKey.Lines.Single(l => l.Id == none.Id).CountedQty);
    }

    [Fact]
    public async Task Delete_open_count_cancels_its_task_and_hides_it()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 2m);
        var svc = f.Get<CycleCountService>();
        var created = await svc.CreateAsync(new CycleCountCreateRequest(), default);

        await svc.DeleteAsync(created.Count.Id, default);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync(created.Count.Id, null, default));
        Assert.Equal(WarehouseTaskStatuses.Cancelled, await f.CountTaskStatusAsync(created.Count.Id));
        // Decisión 24 (Lote 8A): la tarea COUNT se cierra con fecha (AgeHours deja de crecer; llega a sync/warehouse-tasks).
        Assert.NotNull((await f.CountTaskAsync(created.Count.Id)).CompletedAtUtc);
        Assert.Empty(await svc.ListAsync(null, default));
    }

    [Fact]
    public async Task Refresh_resnapshots_stale_lines_and_clears_their_capture()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 5m);
        var svc = f.Get<CycleCountService>();
        var created = await svc.CreateAsync(new CycleCountCreateRequest(), default);
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(created.Lines.Select(l => new CountCaptureItem(l.Id, l.SystemQty)).ToList()), default);
        await f.IssueAsync(f.ProductNoneId, f.PickBin1, 2m);

        var refreshed = await svc.RefreshAsync(created.Count.Id, default);

        var stale = refreshed.Lines.Single(l => l.BinId == f.PickBin1);
        Assert.Equal(6m, stale.SystemQty);
        Assert.Null(stale.CountedQty);
        Assert.False(stale.IsStale);
        var fresh = refreshed.Lines.Single(l => l.BinId == f.PickBin2);
        Assert.Equal(5m, fresh.CountedQty);
    }

    [Fact]
    public async Task Count_task_handler_is_not_completable_from_queue()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        var handler = f.Get<CountTaskHandler>();
        Assert.Equal(WarehouseTaskTypes.Count, handler.TaskType);
        Assert.Equal(PermissionCatalog.WarehouseCount, handler.RequiredPermission);
        Assert.Equal("Las tareas de conteo se completan desde Conteo cíclico.", handler.NotFromQueueMessage);
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => handler.CompleteAsync(new WarehouseTask(), new TaskCompleteRequest(), default));
        Assert.Equal(handler.NotFromQueueMessage, ex.Message);
    }
}

/// <summary>
/// Fixture InMemory del conteo cíclico (al estilo de TripServiceFixture): catálogos y estatus WMS como el seed, un almacén
/// ALM-01 con zona PCK (PICKING) y dos posiciones, productos PN (NONE) y PL (LOT). El inventario entra y sale SOLO por el
/// InventoryLedger real.
/// </summary>
internal sealed class CycleCountFixture : IAsyncDisposable
{
    public const int TenantId = 1;
    private readonly Dictionary<string, int> _lookupIds = new(StringComparer.OrdinalIgnoreCase);

    private CycleCountFixture(TeikemDbContext db, ServiceProvider services, TripTestLookups lookups)
    {
        Db = db;
        Services = services;
        Lookups = lookups;
    }

    public TeikemDbContext Db { get; }
    public ServiceProvider Services { get; }
    public TripTestLookups Lookups { get; }
    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public int WarehouseId => 10;
    public int PickBin1 => 101;
    public int PickBin2 => 102;
    public int ProductNoneId => 301;
    public int ProductLotId => 302;
    public int ProductSerialId => 303;
    public Guid ProductNonePublicId { get; } = Guid.NewGuid();
    public Guid ProductLotPublicId { get; } = Guid.NewGuid();
    public Guid ProductSerialPublicId { get; } = Guid.NewGuid();

    public int LookupId(string domain, string code) => _lookupIds[domain + "|" + code];

    public static async Task<CycleCountFixture> CreateAsync()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1, IsAuthenticated = true, IsPlatformAdmin = true };
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("cycle-count-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new TeikemDbContext(options, tenant);
        var lookups = new TripTestLookups();
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"modules:{TenantId}", new HashSet<string>(new[] { ModuleKeys.WmsLotSerial }, StringComparer.OrdinalIgnoreCase));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton(tenant);
        services.AddSingleton<ILookupCache>(lookups);
        services.AddSingleton<IMemoryCache>(cache);
        services.AddSingleton<ISecurityEventWriter, CycleCountNullSecurityEventWriter>();
        services.AddSingleton<PermissionService>();
        services.AddSingleton(sp => new StatusService(db, tenant, lookups, sp.GetServices<IStatusTransitionEffect>(), sp.GetRequiredService<PermissionService>()));
        services.AddSingleton<INumberSequenceService, InMemoryNumberSequence>();
        services.AddSingleton<IInventoryChangeSink, InventoryChangeSink>();   // Lote 14 (P2): bandeja de cambios del ledger
        services.AddSingleton<InventoryLedger>();
        services.AddSingleton<WarehouseTaskWriter>();
        services.AddSingleton<CountTaskHandler>();
        services.AddSingleton<ITenantClock>(TenantClock.Default);   // Lote 14: "hoy" en hora de Puerto Rico
        services.AddSingleton<CycleCountService>();
        var provider = services.BuildServiceProvider();

        var f = new CycleCountFixture(db, provider, lookups);
        await f.SeedAsync();
        return f;
    }

    // ---------------------------------------------------------------- movimientos por el ledger real

    public Task ReceiveAsync(int productId, int binId, decimal qty, int? lotId = null)
        => PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, productId, qty, LotId: lotId, ToWarehouseId: WarehouseId, ToBinId: binId));

    public Task ReceiveSerialAsync(int productId, int binId, string serial)
        => PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, productId, 1m, SerialNumber: serial, ToWarehouseId: WarehouseId, ToBinId: binId));

    public Task IssueAsync(int productId, int binId, decimal qty, int? lotId = null)
        => PostAsync(new InventoryPosting(InventoryTxnTypes.Issue, productId, qty, LotId: lotId, FromWarehouseId: WarehouseId, FromBinId: binId));

    public async Task ReserveAsync(int productId, int binId, decimal qty, int? lotId = null)
    {
        var ledger = Get<InventoryLedger>();
        await Db.RunInTransactionAsync(ct => ledger.ReserveAsync(new[] { new StockReservation(productId, WarehouseId, binId, lotId, qty) }, ct), default);
        Db.ChangeTracker.Clear();
    }

    private async Task PostAsync(InventoryPosting posting)
    {
        var ledger = Get<InventoryLedger>();
        await Db.RunInTransactionAsync(async ct => { await ledger.PostAsync(new[] { posting }, ct); }, default);
        Db.ChangeTracker.Clear();
    }

    public async Task<int> TxnCountAsync() => await Db.Set<InventoryTransaction>().AsNoTracking().CountAsync();

    public async Task<InventoryTransaction> TxnAsync(long id)
        => await Db.Set<InventoryTransaction>().AsNoTracking().SingleAsync(t => t.InventoryTransactionId == id);

    public async Task<decimal> OnHandAsync(int productId, int binId, int? lotId = null)
    {
        Db.ChangeTracker.Clear();
        return await Db.Set<StockBalance>().AsNoTracking()
            .Where(b => b.ProductId == productId && b.WarehouseBinId == binId && b.LotId == lotId)
            .SumAsync(b => b.QtyOnHand);
    }

    // ---------------------------------------------------------------- apoyo del Lote 14 (lo cambiado y lista paginada)

    /// <summary>Varios movimientos en un solo asiento del ledger.</summary>
    public async Task PostManyAsync(IReadOnlyList<InventoryPosting> postings)
    {
        var ledger = Get<InventoryLedger>();
        await Db.RunInTransactionAsync(async ct => { await ledger.PostAsync(postings, ct); }, default);
        Db.ChangeTracker.Clear();
    }

    /// <summary>Lote del producto (id).</summary>
    public async Task<int> AddLotAsync(int productId, string number)
    {
        var lot = new InventoryLot { ProductId = productId, LotNumber = number, IsActive = true };
        Db.Set<InventoryLot>().Add(lot);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return lot.LotId;
    }

    /// <summary>Corre en el tiempo todos los movimientos existentes (para dejarlos fuera de una ventana).</summary>
    public async Task ShiftTxnsAsync(TimeSpan delta)
    {
        foreach (var t in await Db.Set<InventoryTransaction>().ToListAsync()) t.CreatedAtUtc = t.CreatedAtUtc.Add(delta);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    /// <summary>Zona nueva del almacén con una posición; devuelve el id de la zona.</summary>
    public async Task<int> AddZoneWithBinAsync(string zoneCode, int binId, string binCode)
    {
        var zone = new WarehouseZone { WarehouseId = WarehouseId, Code = zoneCode, Name = zoneCode, ZoneTypeLookupId = LookupId(LookupDomains.ZoneType, ZoneTypes.Reserve), IsActive = true };
        Db.Set<WarehouseZone>().Add(zone);
        await Db.SaveChangesAsync();
        Db.Set<WarehouseBin>().Add(new WarehouseBin { WarehouseBinId = binId, WarehouseZoneId = zone.WarehouseZoneId, WarehouseId = WarehouseId, Code = binCode, IsActive = true });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return zone.WarehouseZoneId;
    }

    /// <summary>n posiciones activas en la zona PCK (ids desde 1000).</summary>
    public async Task<List<int>> AddBinsAsync(int n)
    {
        var ids = Enumerable.Range(1000, n).ToList();
        Db.Set<WarehouseBin>().AddRange(ids.Select(i => new WarehouseBin { WarehouseBinId = i, WarehouseZoneId = 11, WarehouseId = WarehouseId, Code = $"Z-{i}", IsActive = true }));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return ids;
    }

    public async Task SetBinActiveAsync(int binId, bool active)
    {
        (await Db.Set<WarehouseBin>().SingleAsync(b => b.WarehouseBinId == binId)).IsActive = active;
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public async Task SetBarcodeAsync(int productId, string barcode)
    {
        (await Db.Set<Product>().SingleAsync(p => p.ProductId == productId)).Barcode = barcode;
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    /// <summary>Asigna la tarea COUNT del conteo a un usuario (lo crea si no existe).</summary>
    public async Task AssignCountTaskAsync(int cycleCountId, int userId, string fullName)
    {
        if (!await Db.Users.AnyAsync(u => u.Id == userId))
            Db.Users.Add(new Teikem.Domain.Identity.ApplicationUser { Id = userId, UserName = $"u{userId}", Email = $"u{userId}@example.com", FullName = fullName });
        var task = await CountTaskAsync(cycleCountId);
        var tracked = await Db.Set<WarehouseTask>().SingleAsync(t => t.WarehouseTaskId == task.WarehouseTaskId);
        tracked.AssignedToUserId = userId;
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    /// <summary>Estatus destino del historial del conteo, en orden (Lote 14).</summary>
    public async Task<string[]> HistoryAsync(int cycleCountId)
    {
        var entity = LookupId(LookupDomains.EntityType, EntityTypes.CycleCount);
        var ids = await Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == entity && h.EntityId == cycleCountId)
            .OrderBy(h => h.EntityStatusHistoryId).Select(h => h.ToStatusCodeId).ToListAsync();
        var codes = await Db.StatusCodes.AsNoTracking().ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode);
        return ids.Select(i => codes[i]).ToArray();
    }

    public async Task<string> CountTaskStatusAsync(int cycleCountId)
    {
        var task = await CountTaskAsync(cycleCountId);
        return await Db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == task.StatusCodeId).Select(s => s.InternalCode).SingleAsync();
    }

    /// <summary>La tarea COUNT del conteo (sin rastrear).</summary>
    public async Task<WarehouseTask> CountTaskAsync(int cycleCountId)
    {
        var refType = LookupId(LookupDomains.EntityType, EntityTypes.CycleCount);
        var countType = LookupId(LookupDomains.WarehouseTaskType, WarehouseTaskTypes.Count);
        return await Db.Set<WarehouseTask>().AsNoTracking()
            .SingleAsync(t => t.RefEntityLookupId == refType && t.RefId == cycleCountId && t.TaskTypeLookupId == countType);
    }

    // ---------------------------------------------------------------- siembra

    private async Task SeedAsync()
    {
        var id = 1;
        var all = new List<LookupCode>();
        LookupCode L(string entity, string code)
        {
            var l = new LookupCode { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
            all.Add(l);
            _lookupIds[entity + "|" + code] = l.LookupCodeId;
            return l;
        }
        var pipe = L(LookupDomains.StageKind, StageKinds.Pipeline);
        var lat = L(LookupDomains.StageKind, StageKinds.Lateral);
        var term = L(LookupDomains.StageKind, StageKinds.Terminal);
        foreach (var e in new[]
                 {
                     EntityTypes.Warehouse, EntityTypes.WarehouseDock, EntityTypes.Product, EntityTypes.Receipt, EntityTypes.Asn,
                     EntityTypes.InventorySerial, EntityTypes.WarehouseTask, EntityTypes.InventoryTransaction, EntityTypes.StockBalance,
                     EntityTypes.CycleCount, EntityTypes.PickBatch, EntityTypes.PurchaseOrder, EntityTypes.CrossDockAllocation,
                 })
            L(LookupDomains.EntityType, e);
        foreach (var c in new[] { TrackingTypes.None, TrackingTypes.Lot, TrackingTypes.Serial }) L(LookupDomains.TrackingType, c);
        foreach (var c in new[] { ZoneTypes.Picking, ZoneTypes.Reserve, ZoneTypes.Refrigerated, ZoneTypes.Quarantine, ZoneTypes.CrossDock, ZoneTypes.Staging })
            L(LookupDomains.ZoneType, c);
        foreach (var c in new[] { InventoryTxnTypes.Receipt, InventoryTxnTypes.Issue, InventoryTxnTypes.Transfer, InventoryTxnTypes.Adjustment, InventoryTxnTypes.CrossDock })
            L(LookupDomains.InventoryTxnType, c);
        foreach (var c in new[]
                 {
                     AdjustmentReasons.ReceiptVariance, AdjustmentReasons.CountVariance, AdjustmentReasons.Damage, AdjustmentReasons.Loss,
                     AdjustmentReasons.Found, AdjustmentReasons.Expired, AdjustmentReasons.PoShortage, AdjustmentReasons.PickBatchReversal,
                     AdjustmentReasons.Other,
                 })
            L(LookupDomains.AdjustmentReason, c);
        foreach (var c in new[]
                 {
                     WarehouseTaskTypes.Putaway, WarehouseTaskTypes.Pick, WarehouseTaskTypes.Pack, WarehouseTaskTypes.Replenish,
                     WarehouseTaskTypes.Count, WarehouseTaskTypes.Load, WarehouseTaskTypes.CrossDock,
                 })
            L(LookupDomains.WarehouseTaskType, c);
        var uom = L(LookupDomains.UnitOfMeasure, "UN");
        var country = L(LookupDomains.Country, "PR");
        L(LookupDomains.CycleCountOrigin, CycleCountOrigins.Manual);    // Lote 14
        L(LookupDomains.CycleCountOrigin, CycleCountOrigins.Changes);
        Db.LookupCodes.AddRange(all);
        Lookups.Load(all);

        var sid = 100;
        var statusIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void S(string domain, string code, LookupCode kind, int sort, bool initial = false)
        {
            var s = new StatusCode { StatusCodeId = sid++, Entity = domain, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", StageKindLookupId = kind.LookupCodeId, SortOrder = sort, IsInitial = initial, IsActive = true };
            Db.StatusCodes.Add(s);
            statusIds[domain + "|" + code] = s.StatusCodeId;
        }
        S(StatusDomains.WarehouseStatus, WarehouseStatuses.Active, pipe, 1, true);
        S(StatusDomains.WarehouseStatus, WarehouseStatuses.Inactive, term, 2);
        S(StatusDomains.CycleCountStatus, CycleCountStatuses.Open, pipe, 1, true);
        S(StatusDomains.CycleCountStatus, CycleCountStatuses.Counted, pipe, 2);
        S(StatusDomains.CycleCountStatus, CycleCountStatuses.Reconciled, term, 3);
        S(StatusDomains.CycleCountStatus, CycleCountStatuses.ReconciledVariance, term, 4);   // Lote 14 (D7)
        S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Pending, pipe, 1, true);
        S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.InProgress, pipe, 2);
        S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Done, term, 3);
        S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Cancelled, term, 4);
        S(StatusDomains.SerialStatus, SerialStatuses.Available, pipe, 1, true);
        S(StatusDomains.SerialStatus, SerialStatuses.Reserved, lat, 2);
        S(StatusDomains.SerialStatus, SerialStatuses.Shipped, lat, 3);
        S(StatusDomains.SerialStatus, SerialStatuses.Scrapped, term, 4);
        // Lote 14 (seed 3G): 'Diferencia' solo desde Pendiente y Contado.
        foreach (var from in new[] { CycleCountStatuses.Open, CycleCountStatuses.Counted })
            Db.StatusLateralEntries.Add(new StatusLateralEntry
            {
                EntityTypeLookupId = LookupId(LookupDomains.EntityType, EntityTypes.CycleCount),
                LateralStatusCodeId = statusIds[StatusDomains.CycleCountStatus + "|" + CycleCountStatuses.ReconciledVariance],
                FromStatusCodeId = statusIds[StatusDomains.CycleCountStatus + "|" + from], IsAllowed = true,
            });

        Db.Tenants.Add(new Tenant { TenantId = TenantId, Name = "Tenant de prueba", IsActive = true });
        Db.Set<Warehouse>().Add(new Warehouse
        {
            WarehouseId = WarehouseId, PublicId = Guid.NewGuid(), TenantId = TenantId, Code = "ALM-01", Name = "Almacén principal",
            CountryLookupId = country.LookupCodeId, StatusCodeId = statusIds[StatusDomains.WarehouseStatus + "|" + WarehouseStatuses.Active], IsActive = true,
        });
        Db.Set<WarehouseZone>().Add(new WarehouseZone
        {
            WarehouseZoneId = 11, WarehouseId = WarehouseId, Code = "PCK", Name = "Picking", ZoneTypeLookupId = LookupId(LookupDomains.ZoneType, ZoneTypes.Picking), IsActive = true,
        });
        Db.Set<WarehouseBin>().AddRange(
            new WarehouseBin { WarehouseBinId = PickBin1, WarehouseZoneId = 11, WarehouseId = WarehouseId, Code = "A01-R01-N1-P01", IsActive = true },
            new WarehouseBin { WarehouseBinId = PickBin2, WarehouseZoneId = 11, WarehouseId = WarehouseId, Code = "A01-R01-N1-P02", IsActive = true });

        Product P(int productId, Guid publicId, string sku, string tracking) => new()
        {
            ProductId = productId, PublicId = publicId, TenantId = TenantId, Sku = sku, Name = "Producto " + sku,
            BaseUomLookupId = uom.LookupCodeId, TrackingTypeLookupId = LookupId(LookupDomains.TrackingType, tracking),
            PurchaseCost = 10m, IsActive = true,
        };
        Db.Set<Product>().AddRange(
            P(ProductNoneId, ProductNonePublicId, "PN", TrackingTypes.None),
            P(ProductLotId, ProductLotPublicId, "PL", TrackingTypes.Lot),
            P(ProductSerialId, ProductSerialPublicId, "PS", TrackingTypes.Serial));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
    }
}

internal sealed class CycleCountNullSecurityEventWriter : ISecurityEventWriter
{
    public Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default)
        => Task.CompletedTask;
}
