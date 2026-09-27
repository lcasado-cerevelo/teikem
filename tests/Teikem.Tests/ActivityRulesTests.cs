using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Audit;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 7A (P1) — "Actividad reciente": reglas puras (ventana, tope de página, mapeos de motivo y de bodega, transiciones,
/// orden, bandera obligatorio/encendido, módulo visible) y, con WmsFixture (InMemory), el proveedor de Almacén, la compuerta
/// por permiso y módulo del feed y el filtro belowMin de productos. La traducción a SQL la cubre el smoke (paso activity).
/// </summary>
public sealed class ActivityRulesTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 15, 30, 0, DateTimeKind.Utc);

    // ================================================================ ventana y página

    [Theory]
    [InlineData(null, 24)]
    [InlineData("", 24)]
    [InlineData("24h", 24)]
    [InlineData("48h", 48)]
    [InlineData("48H", 48)]
    public void Window_hours_before_now(string? window, int hours)
        => Assert.Equal(Now.AddHours(-hours), ActivityRules.FromUtc(window, Now));

    [Fact]
    public void Today_starts_at_midnight_of_the_tenant_zone()
    {
        Assert.Equal(new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc), ActivityRules.FromUtc("today", Now));
        Assert.Equal(new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc), ActivityRules.FromUtc("today", Now, ActivityRules.TenantZone));
        // UTC−4 (Puerto Rico): a las 15:30 UTC son las 11:30 locales; la medianoche local es 04:00 UTC.
        var pr = TimeZoneInfo.CreateCustomTimeZone("UTC-4", TimeSpan.FromHours(-4), "UTC-4", "UTC-4");
        Assert.Equal(new DateTime(2026, 9, 27, 4, 0, 0, DateTimeKind.Utc), ActivityRules.FromUtc("today", Now, pr));
        // A las 02:00 UTC todavía es el día anterior en UTC−4 (22:00): la ventana empieza el 26 a las 04:00 UTC.
        Assert.Equal(new DateTime(2026, 9, 26, 4, 0, 0, DateTimeKind.Utc),
            ActivityRules.FromUtc("today", new DateTime(2026, 9, 27, 2, 0, 0, DateTimeKind.Utc), pr));
    }

    [Fact]
    public void Unknown_window_is_400()
    {
        var ex = Assert.Throws<ValidationException>(() => ActivityRules.FromUtc("7d", Now));
        Assert.Equal("La ventana debe ser 24h, 48h o today.", ex.Message);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void Page_is_capped_at_50()
    {
        Assert.Equal((0, 50), ActivityRules.Page(0, 0));
        Assert.Equal((0, 50), ActivityRules.Page(-3, -1));
        Assert.Equal((10, 50), ActivityRules.Page(10, 50));
        Assert.Equal((0, 1), ActivityRules.Page(0, 1));
        var ex = Assert.Throws<ValidationException>(() => ActivityRules.Page(0, 51));
        Assert.Equal("El máximo por página es 50.", ex.Message);
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(new[] { "El máximo por página es 50." }, ex.Errors!["take"]);
    }

    // ================================================================ mapeos

    [Theory]
    [InlineData(AdjustmentReasons.ReceiptVariance, true, ActivityEvents.ReceiptVariance)]
    [InlineData(AdjustmentReasons.CountVariance, true, ActivityEvents.CountVariance)]
    [InlineData(AdjustmentReasons.Damage, false, ActivityEvents.InventoryAdjusted)]
    [InlineData(AdjustmentReasons.Loss, false, ActivityEvents.InventoryAdjusted)]
    [InlineData(AdjustmentReasons.Found, false, ActivityEvents.InventoryAdjusted)]
    [InlineData(AdjustmentReasons.Expired, false, ActivityEvents.InventoryAdjusted)]
    [InlineData(AdjustmentReasons.Other, false, ActivityEvents.InventoryAdjusted)]
    [InlineData("damage", false, ActivityEvents.InventoryAdjusted)]
    [InlineData(AdjustmentReasons.Found, true, ActivityEvents.InventoryAdjusted)]   // FOUND de un faltante: además PO_SHORTAGE_RESOLVED
    [InlineData(AdjustmentReasons.Damage, true, ActivityEvents.InventoryAdjusted)]
    [InlineData(AdjustmentReasons.PoShortage, true, null)]                          // faltante de compra: lo cuenta PO_SHORTAGE_RESOLVED
    [InlineData(AdjustmentReasons.PoShortage, false, ActivityEvents.InventoryAdjusted)]  // ajuste manual con motivo fuera de la lista
    [InlineData("THEFT", false, ActivityEvents.InventoryAdjusted)]                  // código nuevo del catálogo, sin documento
    [InlineData("THEFT", true, null)]
    [InlineData(AdjustmentReasons.PickBatchReversal, true, null)]  // reversa de una recolección: la cuenta PICK_CANCELLED
    [InlineData(AdjustmentReasons.PickBatchReversal, false, null)] // motivo de sistema: nunca INVENTORY_ADJUSTED
    [InlineData(null, false, null)]
    public void Adjustment_reason_maps_to_event(string? reason, bool hasRef, string? expected)
        => Assert.Equal(expected, ActivityRules.AdjustmentEventCode(reason, hasRef));

    [Fact]
    public void Transfer_between_warehouses_or_within_one()
    {
        Assert.Equal(ActivityEvents.InventoryTransferred, ActivityRules.TransferEventCode(1, 2));
        Assert.Equal(ActivityEvents.BinMoved, ActivityRules.TransferEventCode(1, 1));
        Assert.Equal(ActivityEvents.BinMoved, ActivityRules.TransferEventCode(null, 1));
    }

    [Theory]
    [InlineData(EntityTypes.Receipt, ReceiptStatuses.Received, null, ActivityEvents.ReceiptConfirmed)]
    [InlineData(EntityTypes.Receipt, ReceiptStatuses.Putaway, null, ActivityEvents.ReceiptPutawayDone)]
    [InlineData(EntityTypes.Receipt, ReceiptStatuses.Open, null, null)]
    [InlineData(EntityTypes.Asn, AsnStatuses.Cancelled, null, ActivityEvents.AsnCancelled)]
    [InlineData(EntityTypes.Asn, AsnStatuses.Received, null, null)]
    [InlineData(EntityTypes.CycleCount, CycleCountStatuses.Counted, null, ActivityEvents.CountFinished)]
    [InlineData(EntityTypes.CycleCount, CycleCountStatuses.Reconciled, null, ActivityEvents.CountReconciled)]
    [InlineData(EntityTypes.PickBatch, PickBatchStatuses.Collected, null, ActivityEvents.PickCollected)]
    [InlineData(EntityTypes.PickBatch, PickBatchStatuses.Packed, null, ActivityEvents.PickPacked)]
    [InlineData(EntityTypes.PickBatch, PickBatchStatuses.Cancelled, null, ActivityEvents.PickCancelled)]
    [InlineData(EntityTypes.PurchaseOrder, PurchaseOrderStatuses.Sent, null, ActivityEvents.PoSent)]
    [InlineData(EntityTypes.PurchaseOrder, PurchaseOrderStatuses.Partial, null, null)]
    [InlineData(EntityTypes.PurchaseOrder, PurchaseOrderStatuses.Received, null, ActivityEvents.PoReceived)]
    [InlineData(EntityTypes.PurchaseOrder, PurchaseOrderStatuses.Cancelled, null, ActivityEvents.PoCancelled)]
    [InlineData(EntityTypes.CrossDockPlan, CrossDockStatuses.Completed, null, ActivityEvents.CrossDockCompleted)]
    [InlineData(EntityTypes.Warehouse, WarehouseStatuses.Inactive, null, ActivityEvents.WarehouseDeactivated)]
    [InlineData(EntityTypes.WarehouseTask, WarehouseTaskStatuses.Done, WarehouseTaskTypes.Putaway, ActivityEvents.PutawayDone)]
    [InlineData(EntityTypes.WarehouseTask, WarehouseTaskStatuses.Done, WarehouseTaskTypes.Replenish, ActivityEvents.ReplenishDone)]
    [InlineData(EntityTypes.WarehouseTask, WarehouseTaskStatuses.Done, WarehouseTaskTypes.Count, null)]
    [InlineData(EntityTypes.WarehouseTask, WarehouseTaskStatuses.Cancelled, WarehouseTaskTypes.Count, ActivityEvents.TaskCancelled)]
    [InlineData(EntityTypes.WarehouseTask, WarehouseTaskStatuses.InProgress, WarehouseTaskTypes.Putaway, null)]
    [InlineData(EntityTypes.TransportOrder, OrderStatuses.Cancelled, null, null)]
    public void Status_transition_maps_to_event(string entityType, string toStatus, string? taskType, string? expected)
        => Assert.Equal(expected, ActivityRules.StatusEventCode(entityType, toStatus, taskType));

    // ================================================================ catálogo, orden y módulos

    [Fact]
    public void Meta_from_extra_json()
    {
        Assert.Equal(new ActivityEventMeta("WAREHOUSE", true, true), ActivityRules.ParseMeta("{\"module\":\"WAREHOUSE\",\"mandatory\":true,\"defaultOn\":true}"));
        Assert.Equal(new ActivityEventMeta("WAREHOUSE", false, false), ActivityRules.ParseMeta("{\"module\":\"WAREHOUSE\",\"mandatory\":false,\"defaultOn\":false}"));
        Assert.Equal(new ActivityEventMeta(null, false, true), ActivityRules.ParseMeta(null));
        Assert.Equal(new ActivityEventMeta(null, false, true), ActivityRules.ParseMeta("no es json"));
        Assert.Equal(new ActivityEventMeta("WAREHOUSE", true, true), ActivityRules.ParseMeta("{\"module\":\"WAREHOUSE\",\"mandatory\":true}"));
    }

    [Fact]
    public void Mandatory_always_shown_optional_only_when_on()
    {
        Assert.True(ActivityRules.IsShown(mandatory: true, defaultOn: false, onlyMandatory: true));
        Assert.True(ActivityRules.IsShown(mandatory: false, defaultOn: true, onlyMandatory: false));
        Assert.False(ActivityRules.IsShown(mandatory: false, defaultOn: true, onlyMandatory: true));
        Assert.False(ActivityRules.IsShown(mandatory: false, defaultOn: false, onlyMandatory: false)); // BIN_MOVED por defecto
    }

    [Fact]
    public void Sorted_newest_first_and_paginated()
    {
        ActivityEventDto E(int minutes, string code, int id) => new(Now.AddMinutes(minutes), code, BusinessModules.Warehouse, false, code,
            EntityTypes.Receipt, id, null, "REC-" + id, null, null, null);
        var events = new[] { E(-30, "B", 1), E(-5, "A", 2), E(-30, "A", 3), E(-60, "C", 4) };
        var (total, items) = ActivityRules.Paginate(events, 1, 2);
        Assert.Equal(4, total);
        Assert.Equal(new[] { 3, 1 }, items.Select(i => i.EntityId).ToArray());
        Assert.Equal(new[] { 2, 3, 1, 4 }, ActivityRules.Sort(events).Select(i => i.EntityId).ToArray());
    }

    [Fact]
    public void Visible_module_or_403()
    {
        var visible = new[] { BusinessModules.Warehouse, BusinessModules.Operations };
        Assert.Equal(BusinessModules.Warehouse, ActivityRules.ResolveModule(null, visible));
        Assert.Equal(BusinessModules.Operations, ActivityRules.ResolveModule("operations", visible));
        Assert.Null(ActivityRules.ResolveModule(null, Array.Empty<string>()));
        var ex = Assert.Throws<ForbiddenException>(() => ActivityRules.ResolveModule("accounting", visible));
        Assert.Equal("No tiene permiso para ver la actividad del módulo ACCOUNTING.", ex.Message);
        Assert.Equal(403, ex.StatusCode);
        Assert.True(ActivityRules.ModuleRank(BusinessModules.Warehouse) < ActivityRules.ModuleRank(BusinessModules.Operations));
        Assert.True(ActivityRules.ModuleRank(BusinessModules.Operations) < ActivityRules.ModuleRank(BusinessModules.Accounting));
        Assert.Equal(ModuleKeys.WmsLotSerial, ActivityRules.TenantModuleFor(BusinessModules.Warehouse));
        Assert.Null(ActivityRules.TenantModuleFor(BusinessModules.Operations));
    }

    [Fact]
    public void Detail_texts()
    {
        Assert.Equal("ALM-01 · 3 líneas", ActivityRules.Detail("ALM-01", null, " ", ActivityRules.Lines(3, "es")));
        Assert.Equal("1 line", ActivityRules.Lines(1, "en"));
        Assert.Null(ActivityRules.Detail(null, ""));
        Assert.Equal("Diferencia neta +2.5", ActivityRules.NetVariance(2.5m, "es"));
        Assert.Equal("Net variance -1", ActivityRules.NetVariance(-1m, "en"));
    }

    // ================================================================ proveedor y feed (InMemory)

    private static readonly (string Code, bool Mandatory, bool DefaultOn)[] Catalog =
    {
        (ActivityEvents.ReceiptConfirmed, true, true), (ActivityEvents.ReceiptVariance, true, true), (ActivityEvents.ReceiptPutawayDone, false, true),
        (ActivityEvents.AsnCancelled, false, true), (ActivityEvents.PutawayDone, false, true), (ActivityEvents.ReplenishDone, false, true),
        (ActivityEvents.TaskCancelled, false, true), (ActivityEvents.CountFinished, false, true), (ActivityEvents.CountReconciled, true, true),
        (ActivityEvents.CountVariance, true, true), (ActivityEvents.InventoryAdjusted, true, true), (ActivityEvents.InventoryTransferred, false, true),
        (ActivityEvents.BinMoved, false, false), (ActivityEvents.PickCollected, false, true), (ActivityEvents.PickPacked, false, true),
        (ActivityEvents.PickCancelled, true, true), (ActivityEvents.PoSent, false, true), (ActivityEvents.PoReceived, false, true),
        (ActivityEvents.PoCancelled, true, true), (ActivityEvents.PoShortageResolved, false, true), (ActivityEvents.CrossDockCompleted, false, true),
        (ActivityEvents.ProductDeactivated, false, true), (ActivityEvents.WarehouseDeactivated, true, true),
    };

    /// <summary>
    /// P0: el seed siembra los 23 eventos de Almacén en el orden del catálogo, con la bandera de obligatorio y el encendido por
    /// defecto del maestro (los mismos que usa este archivo), el dominio ActivityEventType y el índice por tenant y fecha.
    /// </summary>
    [Fact]
    public void Seed_catalog_matches_constants_and_flags()
    {
        var root = TripCatalogTests.RepoRoot();
        var seed = File.ReadAllText(Path.Combine(root, "Diseño", "logistica-db-seed.sql"));
        var structure = File.ReadAllText(Path.Combine(root, "Diseño", "logistica-db-estructura.sql"));
        Assert.Contains($"('{LookupDomains.ActivityEventType}',1,", seed);
        Assert.Contains("CREATE INDEX IX_EntityStatusHistory_TenantDate ON dbo.EntityStatusHistory(TenantId, ChangedAtUtc);", structure);

        var start = seed.IndexOf("AS s ON t.Entity = 'ActivityEventType'", StringComparison.Ordinal);
        Assert.True(start > 0, "No está el MERGE de ActivityEventType en el seed.");
        var block = seed[seed.LastIndexOf("MERGE dbo.LookupCode", start, StringComparison.Ordinal)..start];
        var rows = System.Text.RegularExpressions.Regex.Matches(block, @"\('([A-Z_]+)',N'[^']+',N'[^']+',([01]),([01]),(\d+)\)")
            .Select(m => (Code: m.Groups[1].Value, Mandatory: m.Groups[2].Value == "1", DefaultOn: m.Groups[3].Value == "1",
                Sort: int.Parse(m.Groups[4].Value)))
            .ToList();
        Assert.Equal(ActivityEvents.Warehouse, rows.Select(r => r.Code).ToList());
        Assert.Equal(Enumerable.Range(1, 23), rows.Select(r => r.Sort));
        Assert.Equal(Catalog.Select(c => (c.Code, c.Mandatory, c.DefaultOn)), rows.Select(r => (r.Code, r.Mandatory, r.DefaultOn)));
    }

    private static async Task<WmsFixture> CreateAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ProductService>();
            s.AddSingleton<IActivityEventProvider, WarehouseActivityProvider>();
            s.AddSingleton<ActivityFeedService>();
        });
        var existing = await f.Db.LookupCodes.AsNoTracking().ToListAsync();
        var id = 5000;
        var activity = Catalog.Select(c => new LookupCode
        {
            LookupCodeId = id++, Entity = ActivityRules.CatalogDomain, InternalCode = c.Code, IsActive = true, IsSystem = true,
            LabelJson = $"{{\"es\":\"es {c.Code}\",\"en\":\"en {c.Code}\"}}",
            ExtraJson = $"{{\"module\":\"WAREHOUSE\",\"mandatory\":{(c.Mandatory ? "true" : "false")},\"defaultOn\":{(c.DefaultOn ? "true" : "false")}}}",
        }).ToList();
        // Bitácora (PRODUCT_DEACTIVATED): acción DELETE y el EntityType propio de la categoría (Lote 7A).
        activity.Add(new LookupCode { LookupCodeId = id++, Entity = LookupDomains.AuditAction, InternalCode = AuditActions.Delete, IsActive = true, LabelJson = "{\"es\":\"DELETE\"}" });
        activity.Add(new LookupCode { LookupCodeId = id++, Entity = LookupDomains.EntityType, InternalCode = EntityTypes.ProductCategory, IsActive = true, LabelJson = "{\"es\":\"PRODUCT_CATEGORY\"}" });
        f.Lookups.Load(existing.Concat(activity));
        // Quien hace los eventos (ChangedBy / CreatedBy = usuario de la sesión).
        f.Db.Users.Add(new ApplicationUser { Id = f.Tenant.UserId!.Value, UserName = "ana", Email = "ana@teikem.test", FullName = "Ana Operadora" });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return f;
    }

    private static int LookupIdOf(WmsFixture f, string domain, string code)
        => f.Lookups.TryGetIdAsync(domain, code, default).GetAwaiter().GetResult()!.Value;

    private static void History(WmsFixture f, string entityType, int entityId, string domain, string toStatus, DateTime atUtc)
        => f.Db.EntityStatusHistories.Add(new EntityStatusHistory
        {
            TenantId = f.Tenant.TenantId!.Value, EntityTypeLookupId = f.LookupId(LookupDomains.EntityType, entityType), EntityId = entityId,
            ToStatusCodeId = f.StatusId(domain, toStatus), ChangedAtUtc = atUtc, ChangedBy = f.Tenant.UserId,
        });

    [Fact]
    public async Task Warehouse_provider_reads_history_and_ledger_in_the_window()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var w2 = await f.AddWarehouseAsync("W2");
        var b1 = await f.AddBinAsync(await f.AddZoneAsync(w1, "PCK", ZoneTypes.Picking), "P-01");
        var b1b = await f.AddBinAsync(await f.AddZoneAsync(w1, "RSV", ZoneTypes.Reserve), "R-01");
        var b2 = await f.AddBinAsync(await f.AddZoneAsync(w2, "PCK", ZoneTypes.Picking), "P-02");
        var p = await f.AddProductAsync("SKU-A");

        var received = await f.AddReceiptAsync(w1, ReceiptStatuses.Received);
        var old = await f.AddReceiptAsync(w1, ReceiptStatuses.Received);
        History(f, EntityTypes.Receipt, received.ReceiptHeaderId, StatusDomains.ReceiptStatus, ReceiptStatuses.Received, DateTime.UtcNow.AddMinutes(-10));
        History(f, EntityTypes.Receipt, old.ReceiptHeaderId, StatusDomains.ReceiptStatus, ReceiptStatuses.Received, DateTime.UtcNow.AddHours(-30));
        await f.Db.SaveChangesAsync();

        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 10m, ToWarehouseId: w1.WarehouseId, ToBinId: b1.WarehouseBinId));
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment, p.ProductId, 2m, FromWarehouseId: w1.WarehouseId, FromBinId: b1.WarehouseBinId,
            ReasonCode: AdjustmentReasons.Damage));
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, p.ProductId, 3m, FromWarehouseId: w1.WarehouseId, FromBinId: b1.WarehouseBinId,
            ToWarehouseId: w2.WarehouseId, ToBinId: b2.WarehouseBinId));
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, p.ProductId, 1m, FromWarehouseId: w1.WarehouseId, FromBinId: b1.WarehouseBinId,
            ToWarehouseId: w1.WarehouseId, ToBinId: b1b.WarehouseBinId));

        var page = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse), default);
        Assert.Equal(new[] { BusinessModules.Warehouse }, page.VisibleModules);
        var confirmed = Assert.Single(page.Items, i => i.Code == ActivityEvents.ReceiptConfirmed);
        Assert.True(confirmed.Mandatory);
        Assert.Equal("es " + ActivityEvents.ReceiptConfirmed, confirmed.Label);
        Assert.Equal(received.ReceiptHeaderId, confirmed.EntityId);
        Assert.Equal(received.PublicId, confirmed.PublicId);
        Assert.Equal(received.Number, confirmed.Reference);
        Assert.StartsWith("W1", confirmed.Detail);
        Assert.Equal((f.Tenant.UserId, "Ana Operadora"), (confirmed.UserId, confirmed.UserName));   // quién: EntityStatusHistory.ChangedBy
        var adjusted = Assert.Single(page.Items, i => i.Code == ActivityEvents.InventoryAdjusted);
        Assert.Equal((f.Tenant.UserId, "Ana Operadora"), (adjusted.UserId, adjusted.UserName));     // quién: InventoryTransaction.CreatedBy
        Assert.Equal((EntityTypes.Product, p.PublicId, "SKU-A"), (adjusted.EntityType, adjusted.PublicId, adjusted.Reference));
        Assert.Contains("-2", adjusted.Detail);
        var transferred = Assert.Single(page.Items, i => i.Code == ActivityEvents.InventoryTransferred);
        Assert.Contains("W1 → W2", transferred.Detail);
        Assert.DoesNotContain(page.Items, i => i.Code == ActivityEvents.BinMoved);   // apagado por defecto
        Assert.Equal(3, page.Total);                                                  // el recibo de hace 30 h queda fuera

        var wide = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(null, "48h"), default);
        Assert.Equal(2, wide.Items.Count(i => i.Code == ActivityEvents.ReceiptConfirmed));

        var mandatory = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(null, "24h", OnlyMandatory: true), default);
        Assert.All(mandatory.Items, i => Assert.True(i.Mandatory));
        Assert.DoesNotContain(mandatory.Items, i => i.Code == ActivityEvents.InventoryTransferred);
        Assert.Equal(2, mandatory.Total);

        var first = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(null, "24h", false, 0, 1), default);
        Assert.Equal(3, first.Total);
        Assert.Single(first.Items);
    }

    [Fact]
    public async Task Warehouse_provider_maps_receipt_and_count_variances()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var b1 = await f.AddBinAsync(await f.AddZoneAsync(w1, "PCK", ZoneTypes.Picking), "P-01");
        var pa = await f.AddProductAsync("SKU-A");
        var pb = await f.AddProductAsync("SKU-B");
        var r1 = await f.AddReceiptAsync(w1, ReceiptStatuses.Received);
        var r2 = await f.AddReceiptAsync(w1, ReceiptStatuses.Received);
        var cc = new Teikem.Domain.Wms.CycleCount
        {
            TenantId = f.Tenant.TenantId!.Value, WarehouseId = w1.WarehouseId, Number = "CC-00077",
            StatusCodeId = f.StatusId(StatusDomains.CycleCountStatus, CycleCountStatuses.Reconciled), CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<Teikem.Domain.Wms.CycleCount>().Add(cc);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        InventoryPosting Var(string reason, string refType, int refId, int productId, decimal qty)
            => new(InventoryTxnTypes.Adjustment, productId, qty, ToWarehouseId: w1.WarehouseId, ToBinId: b1.WarehouseBinId,
                RefEntityType: refType, RefId: refId, ReasonCode: reason);
        // Un solo asiento: todas las filas comparten instante y usuario; solo el documento de origen separa las operaciones.
        await f.PostAsync(
            Var(AdjustmentReasons.ReceiptVariance, EntityTypes.Receipt, r1.ReceiptHeaderId, pa.ProductId, 2m),
            Var(AdjustmentReasons.ReceiptVariance, EntityTypes.Receipt, r1.ReceiptHeaderId, pb.ProductId, 1m),
            Var(AdjustmentReasons.ReceiptVariance, EntityTypes.Receipt, r2.ReceiptHeaderId, pa.ProductId, 4m),
            Var(AdjustmentReasons.CountVariance, EntityTypes.CycleCount, cc.CycleCountId, pa.ProductId, 5m),
            Var(AdjustmentReasons.ReceiptVariance, EntityTypes.Receipt, 987654, pb.ProductId, 7m));   // documento inexistente

        foreach (var onlyMandatory in new[] { false, true })
        {
            var page = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse, "24h", onlyMandatory), default);
            var receipts = page.Items.Where(i => i.Code == ActivityEvents.ReceiptVariance).OrderBy(i => i.EntityId).ToList();
            Assert.Equal(2, receipts.Count);                                   // uno por recibo; el RefId inexistente se descarta
            Assert.All(receipts, e => Assert.Equal(EntityTypes.Receipt, e.EntityType));
            Assert.All(receipts, e => Assert.True(e.Mandatory));
            var first = Assert.Single(receipts, e => e.EntityId == r1.ReceiptHeaderId);
            Assert.Equal((r1.PublicId, r1.Number), (first.PublicId!.Value, first.Reference));
            Assert.Contains("2 productos", first.Detail);
            Assert.Contains("Diferencia neta +3", first.Detail);
            var second = Assert.Single(receipts, e => e.EntityId == r2.ReceiptHeaderId);
            Assert.Equal((r2.PublicId, r2.Number), (second.PublicId!.Value, second.Reference));
            Assert.Contains("1 producto", second.Detail);
            Assert.Contains("Diferencia neta +4", second.Detail);
            var count = Assert.Single(page.Items, i => i.Code == ActivityEvents.CountVariance);
            Assert.Equal((EntityTypes.CycleCount, cc.CycleCountId, "CC-00077", true), (count.EntityType, count.EntityId, count.Reference, count.Mandatory));
            Assert.DoesNotContain(page.Items, i => i.EntityId == 987654);
        }
    }

    [Fact]
    public async Task Found_adjustment_of_a_shortage_is_an_inventory_adjustment_and_a_shortage_resolution()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var b1 = await f.AddBinAsync(await f.AddZoneAsync(w1, "PCK", ZoneTypes.Picking), "P-01");
        var p = await f.AddProductAsync("SKU-A");
        var po = new Teikem.Domain.Wms.PurchaseOrder
        {
            PublicId = Guid.NewGuid(), TenantId = f.Tenant.TenantId!.Value, SupplierId = 1, WarehouseId = w1.WarehouseId, Number = "PO-00042",
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow), StatusCodeId = f.StatusId(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Partial),
            IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<Teikem.Domain.Wms.PurchaseOrder>().Add(po);
        await f.Db.SaveChangesAsync();
        var line = new Teikem.Domain.Wms.PurchaseOrderLine { PurchaseOrderId = po.PurchaseOrderId, ProductId = p.ProductId, QtyOrdered = 5m, QtyReceived = 4m, UnitCost = 1m };
        f.Db.Set<Teikem.Domain.Wms.PurchaseOrderLine>().Add(line);
        await f.Db.SaveChangesAsync();
        f.Db.Set<Teikem.Domain.Wms.PurchaseOrderShortageResolution>().Add(new Teikem.Domain.Wms.PurchaseOrderShortageResolution
        {
            TenantId = f.Tenant.TenantId!.Value, PurchaseOrderId = po.PurchaseOrderId, PurchaseOrderLineId = line.PurchaseOrderLineId,
            ActionLookupId = f.LookupId(LookupDomains.ShortageAction, ShortageActions.ManualAdjustment), Quantity = 1m,
            CreatedAtUtc = DateTime.UtcNow, CreatedBy = f.Tenant.UserId,
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment, p.ProductId, 1m, ToWarehouseId: w1.WarehouseId, ToBinId: b1.WarehouseBinId,
            RefEntityType: EntityTypes.PurchaseOrder, RefId: po.PurchaseOrderId, ReasonCode: AdjustmentReasons.Found));

        var page = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse), default);
        var resolved = Assert.Single(page.Items, i => i.Code == ActivityEvents.PoShortageResolved);
        Assert.Equal((EntityTypes.PurchaseOrder, po.PurchaseOrderId, po.PublicId, "PO-00042", false),
            (resolved.EntityType, resolved.EntityId, resolved.PublicId!.Value, resolved.Reference, resolved.Mandatory));
        Assert.Contains("SKU-A", resolved.Detail);
        Assert.Equal((f.Tenant.UserId, "Ana Operadora"), (resolved.UserId, resolved.UserName));
        var adjusted = Assert.Single(page.Items, i => i.Code == ActivityEvents.InventoryAdjusted);   // el +1 FOUND es obligatorio
        Assert.Equal((EntityTypes.Product, p.ProductId, true), (adjusted.EntityType, adjusted.EntityId, adjusted.Mandatory));
        Assert.Contains("+1", adjusted.Detail);

        var mandatory = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse, "24h", OnlyMandatory: true), default);
        Assert.Contains(mandatory.Items, i => i.Code == ActivityEvents.InventoryAdjusted);
        Assert.DoesNotContain(mandatory.Items, i => i.Code == ActivityEvents.PoShortageResolved);
    }

    [Fact]
    public async Task Product_deactivated_comes_from_product_audit_only_even_if_reactivated()
    {
        await using var f = await CreateAsync();
        var p = await f.AddProductAsync("SKU-A");   // activo otra vez: se dio de baja y se reactivó dentro de la ventana
        var old = await f.AddProductAsync("SKU-OLD", isActive: false);
        // Categoría con el mismo id que el producto: su baja se audita como PRODUCT_CATEGORY, no como PRODUCT.
        f.Db.Set<Teikem.Domain.Wms.ProductCategory>().Add(new Teikem.Domain.Wms.ProductCategory
        {
            ProductCategoryId = old.ProductId, TenantId = f.Tenant.TenantId!.Value, Name = "Cat", IsActive = false,
        });
        var delete = LookupIdOf(f, LookupDomains.AuditAction, AuditActions.Delete);
        AuditLog Audit(string entityType, int id, DateTime at) => new()
        {
            TenantId = f.Tenant.TenantId!.Value, EntityTypeLookupId = LookupIdOf(f, LookupDomains.EntityType, entityType), EntityId = id,
            ActionLookupId = delete, UserId = f.Tenant.UserId, ChangesJson = "{\"IsActive\":{\"old\":true,\"new\":false}}", CreatedAtUtc = at,
        };
        f.Db.AuditLogs.AddRange(
            Audit(EntityTypes.Product, p.ProductId, DateTime.UtcNow.AddMinutes(-20)),
            Audit(EntityTypes.Product, old.ProductId, DateTime.UtcNow.AddHours(-30)),          // fuera de la ventana de 24 h
            Audit(EntityTypes.ProductCategory, old.ProductId, DateTime.UtcNow.AddMinutes(-5)));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var page = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse), default);
        var deactivated = Assert.Single(page.Items, i => i.Code == ActivityEvents.ProductDeactivated);
        Assert.Equal((EntityTypes.Product, p.ProductId, p.PublicId, "SKU-A"), (deactivated.EntityType, deactivated.EntityId, deactivated.PublicId!.Value, deactivated.Reference));
        Assert.Equal((f.Tenant.UserId, "Ana Operadora"), (deactivated.UserId, deactivated.UserName));
    }

    [Fact]
    public async Task Crossdock_completed_only_with_the_crossdock_module()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var plan = new Teikem.Domain.Wms.CrossDockPlan
        {
            TenantId = f.Tenant.TenantId!.Value, WarehouseId = w1.WarehouseId, Number = "XD-1",
            StatusCodeId = f.StatusId(StatusDomains.CrossDockStatus, CrossDockStatuses.Completed), CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<Teikem.Domain.Wms.CrossDockPlan>().Add(plan);
        await f.Db.SaveChangesAsync();
        History(f, EntityTypes.CrossDockPlan, plan.CrossDockPlanId, StatusDomains.CrossDockStatus, CrossDockStatuses.Completed, DateTime.UtcNow.AddMinutes(-5));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var on = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse), default);
        var completed = Assert.Single(on.Items, i => i.Code == ActivityEvents.CrossDockCompleted);
        Assert.Equal((EntityTypes.CrossDockPlan, plan.CrossDockPlanId, "XD-1"), (completed.EntityType, completed.EntityId, completed.Reference));
        Assert.StartsWith("W1", completed.Detail);

        f.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.Purchasing);   // CROSSDOCK apagado
        var off = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse), default);
        Assert.Equal(new[] { BusinessModules.Warehouse }, off.VisibleModules);   // la pestaña sigue visible
        Assert.DoesNotContain(off.Items, i => i.Code == ActivityEvents.CrossDockCompleted);
    }

    [Fact]
    public async Task Feed_ignores_events_of_other_tenants()
    {
        await using var f = await CreateAsync();
        using (f.AsTenant(WmsFixture.OtherTenantId))
        {
            var w = await f.AddWarehouseAsync("WX");
            var b = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-X");
            var p = await f.AddProductAsync("SKU-X");
            var r = await f.AddReceiptAsync(w, ReceiptStatuses.Received);
            History(f, EntityTypes.Receipt, r.ReceiptHeaderId, StatusDomains.ReceiptStatus, ReceiptStatuses.Received, DateTime.UtcNow.AddMinutes(-5));
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
            await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 5m, ToWarehouseId: w.WarehouseId, ToBinId: b.WarehouseBinId));
            await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment, p.ProductId, 1m, FromWarehouseId: w.WarehouseId, FromBinId: b.WarehouseBinId,
                ReasonCode: AdjustmentReasons.Damage));
            await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment, p.ProductId, 1m, ToWarehouseId: w.WarehouseId, ToBinId: b.WarehouseBinId,
                RefEntityType: EntityTypes.Receipt, RefId: r.ReceiptHeaderId, ReasonCode: AdjustmentReasons.ReceiptVariance));
            // Control: el otro tenant sí ve sus eventos (la prueba no pasa en vacío).
            f.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.Purchasing, ModuleKeys.CrossDock);
            var own = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse, "48h"), default);
            Assert.Contains(own.Items, i => i.Code == ActivityEvents.ReceiptConfirmed);
        }
        var page = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse, "48h"), default);
        Assert.Equal(0, page.Total);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Feed_requires_the_module_permission_and_the_wms_module()
    {
        await using var f = await CreateAsync();
        var feed = f.Get<ActivityFeedService>();

        f.SetPermissions(PermissionCatalog.AnalyticsView);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => feed.GetAsync(new ActivityQuery("warehouse"), default));
        Assert.Equal("No tiene permiso para ver la actividad del módulo WAREHOUSE.", ex.Message);
        var none = await feed.GetAsync(new ActivityQuery(), default);
        Assert.Empty(none.VisibleModules);
        Assert.Equal(0, none.Total);

        f.SetPermissions(PermissionCatalog.AnalyticsView, PermissionCatalog.InventoryView);
        Assert.Equal(new[] { BusinessModules.Warehouse }, (await feed.GetAsync(new ActivityQuery(), default)).VisibleModules);

        f.SetModules(ModuleKeys.Purchasing);   // WMS_LOTSERIAL apagado: la pestaña Almacén desaparece
        await Assert.ThrowsAsync<ForbiddenException>(() => feed.GetAsync(new ActivityQuery(BusinessModules.Warehouse), default));
        await Assert.ThrowsAsync<ValidationException>(() => feed.GetAsync(new ActivityQuery(Take: 51), default));
    }

    [Fact]
    public async Task Products_below_min_filter_matches_is_below_min()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var w2 = await f.AddWarehouseAsync("W2");
        var b1 = await f.AddBinAsync(await f.AddZoneAsync(w1, "PCK", ZoneTypes.Picking), "P-01");
        var b2 = await f.AddBinAsync(await f.AddZoneAsync(w2, "PCK", ZoneTypes.Picking), "P-02");
        var low = await f.AddProductAsync("LOW");          // mínimo 5, disponible 4 − 1 reservado = 3
        var ok = await f.AddProductAsync("OK");            // mínimo 2, disponible 2
        var empty = await f.AddProductAsync("EMPTY");      // mínimo 1, sin saldo
        var noMin = await f.AddProductAsync("NOMIN");      // sin mínimo
        var inactive = await f.AddProductAsync("OFF", isActive: false);
        var split = await f.AddProductAsync("SPLIT");      // mínimo 4: 3 en W1 y 3 en W2 (total 6)
        foreach (var (prod, min) in new[] { (low, 5m), (ok, 2m), (empty, 1m), (inactive, 9m), (split, 4m) })
            (await f.Db.Products.AsTracking().SingleAsync(x => x.ProductId == prod.ProductId)).MinQty = min;
        await f.Db.SaveChangesAsync();
        InventoryPosting In(int productId, Teikem.Domain.Wms.WarehouseBin bin, decimal qty)
            => new(InventoryTxnTypes.Receipt, productId, qty, ToWarehouseId: bin.WarehouseId, ToBinId: bin.WarehouseBinId);
        await f.PostAsync(In(low.ProductId, b1, 4m), In(ok.ProductId, b1, 2m), In(noMin.ProductId, b1, 1m), In(split.ProductId, b1, 3m), In(split.ProductId, b2, 3m));
        await f.ReserveAsync(new StockReservation(low.ProductId, w1.WarehouseId, b1.WarehouseBinId, null, 1m));

        var products = f.Get<ProductService>();
        var below = await products.ListAsync(new ProductListQuery(BelowMin: true), InventoryScope.Any, default);
        Assert.Equal(new[] { "EMPTY", "LOW" }, below.Items.Select(i => i.Sku).ToArray());
        Assert.All(below.Items, i => Assert.True(i.IsBelowMin));
        var count = await products.ListAsync(new ProductListQuery(BelowMin: true, Take: 1), InventoryScope.Any, default);
        Assert.Equal(2, count.Total);
        var inW1 = await products.ListAsync(new ProductListQuery(WarehousePublicId: w1.PublicId, BelowMin: true), InventoryScope.Any, default);
        Assert.Equal(new[] { "EMPTY", "LOW", "SPLIT" }, inW1.Items.Select(i => i.Sku).ToArray());
        Assert.All(inW1.Items, i => Assert.True(i.IsBelowMin));
    }
}
