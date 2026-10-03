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
        // Lote 15: el servicio pasa la zona de la compañía (ITenantClock, hora de Puerto Rico): medianoche local = 04:00 UTC.
        Assert.Equal(new DateTime(2026, 9, 27, 4, 0, 0, DateTimeKind.Utc), ActivityRules.FromUtc("today", Now, Teikem.Domain.Common.LocalDay.DefaultZone));
        Assert.Equal(new DateTime(2026, 9, 27, 4, 0, 0, DateTimeKind.Utc), ActivityRules.FromUtc("today", Now, Teikem.Infrastructure.Abstractions.TenantClock.Default.Zone));
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
    [InlineData(EntityTypes.Receipt, ReceiptStatuses.ReceivedWithVariance, null, ActivityEvents.ReceiptConfirmed)]   // Lote 13
    [InlineData(EntityTypes.Receipt, ReceiptStatuses.Expected, null, null)]
    [InlineData(EntityTypes.Receipt, ReceiptStatuses.Receiving, null, null)]
    [InlineData(EntityTypes.Receipt, ReceiptStatuses.Discrepancy, null, null)]
    [InlineData(EntityTypes.Asn, AsnStatuses.Cancelled, null, ActivityEvents.AsnCancelled)]
    [InlineData(EntityTypes.Asn, AsnStatuses.Received, null, null)]
    [InlineData(EntityTypes.CycleCount, CycleCountStatuses.Counted, null, ActivityEvents.CountFinished)]
    [InlineData(EntityTypes.CycleCount, CycleCountStatuses.Reconciled, null, ActivityEvents.CountReconciled)]
    [InlineData(EntityTypes.CycleCount, CycleCountStatuses.ReconciledVariance, null, ActivityEvents.CountReconciled)]   // Lote 14 (D7)
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
    public void Tenant_override_disables_optionals_and_switches_default_but_never_degrades_mandatory()
    {
        var optional = new ActivityEventMeta("WAREHOUSE", false, true);
        var mandatory = new ActivityEventMeta("WAREHOUSE", true, true);
        Assert.Equal(optional, ActivityRules.ApplyOverride(optional, true, null));
        Assert.Null(ActivityRules.ApplyOverride(optional, false, null));                              // deshabilitado: fuera
        Assert.Equal(mandatory, ActivityRules.ApplyOverride(mandatory, false, null));                 // obligatorio: sigue
        Assert.Equal(mandatory, ActivityRules.ApplyOverride(mandatory, true, "{\"mandatory\":false}"));   // no se degrada
        Assert.Equal(optional with { DefaultOn = false }, ActivityRules.ApplyOverride(optional, true, "{\"defaultOn\":false}"));
        Assert.Equal(optional, ActivityRules.ApplyOverride(optional, true, "{\"otro\":1}"));        // sin defaultOn: el de la base
        Assert.Equal(optional, ActivityRules.ApplyOverride(optional, true, "no es json"));
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
        var rows = System.Text.RegularExpressions.Regex.Matches(block, @"\('([A-Z_]+)',N'([^']+)',N'([^']+)',([01]),([01]),(\d+)\)")
            .Select(m => (Code: m.Groups[1].Value, Es: m.Groups[2].Value, En: m.Groups[3].Value, Mandatory: m.Groups[4].Value == "1",
                DefaultOn: m.Groups[5].Value == "1", Sort: int.Parse(m.Groups[6].Value)))
            .ToList();
        Assert.Equal(ActivityEvents.Warehouse, rows.Select(r => r.Code).ToList());
        Assert.Equal(Enumerable.Range(1, 23), rows.Select(r => r.Sort));
        Assert.Equal(Catalog.Select(c => (c.Code, c.Mandatory, c.DefaultOn)), rows.Select(r => (r.Code, r.Mandatory, r.DefaultOn)));

        // El maestro es la referencia única: etiqueta es / en y bandera de obligatorio de la tabla 'Catálogo inicial — Almacén'.
        var master = MasterWarehouseCatalog(root);
        Assert.Equal(rows.Select(r => r.Code), master.Select(m => m.Code));
        Assert.Equal(master.Select(m => (m.Code, m.Es, m.En, m.Mandatory)), rows.Select(r => (r.Code, r.Es, r.En, r.Mandatory)));
    }

    /// <summary>Filas de la tabla 'Catálogo inicial — Almacén' del maestro: código, etiqueta es / en y obligatorio (Sí/No).</summary>
    private static List<(string Code, string Es, string En, bool Mandatory)> MasterWarehouseCatalog(string root)
    {
        var text = File.ReadAllText(Path.Combine(root, "Diseño", "logistica-funcionalidades-maestro.md"));
        var start = text.IndexOf("**Catálogo inicial — Almacén**", StringComparison.Ordinal);
        Assert.True(start > 0, "No está la tabla 'Catálogo inicial — Almacén' en el maestro.");
        var end = text.IndexOf("**Catálogo inicial — Operación**", start, StringComparison.Ordinal);
        Assert.True(end > start, "No está el cierre de la tabla de Almacén en el maestro.");
        var result = new List<(string, string, string, bool)>();
        foreach (var line in text[start..end].Split('\n'))
        {
            var cells = line.Trim().Split('|');
            if (cells.Length < 7) continue;
            var code = System.Text.RegularExpressions.Regex.Match(cells[1].Trim(), @"^`([A-Z_]+)`$");
            if (!code.Success) continue;
            var labels = cells[3].Split(" / ");
            Assert.True(labels.Length == 2, $"Etiqueta es / en mal formada en el maestro: {cells[3]}");
            var flag = cells[4].Trim();
            Assert.True(flag.StartsWith("Sí") || flag.StartsWith("No"), $"Bandera de obligatorio no reconocida en el maestro: {flag}");
            result.Add((code.Groups[1].Value, labels[0].Trim(), labels[1].Trim(), flag.StartsWith("Sí")));
        }
        return result;
    }

    /// <summary>Escritor de eventos de seguridad que guarda las llamadas (para probar el PERMISSION_DENIED del 403).</summary>
    private sealed class RecordingSecurityEventWriter : ISecurityEventWriter
    {
        public List<(string EventType, string Outcome)> Calls { get; } = new();

        public Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default)
        {
            Calls.Add((eventType, outcome));
            return Task.CompletedTask;
        }
    }

    private static async Task<WmsFixture> CreateAsync(bool binMovedOn = false, ISecurityEventWriter? security = null)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ProductService>();
            s.AddSingleton<IActivityEventProvider, WarehouseActivityProvider>();
            s.AddSingleton<ActivityFeedService>();
            if (security is not null) s.AddSingleton(security);   // el último registro gana
        });
        var existing = await f.Db.LookupCodes.AsNoTracking().ToListAsync();
        var id = 5000;
        var activity = Catalog.Select(c => new LookupCode
        {
            LookupCodeId = id++, Entity = ActivityRules.CatalogDomain, InternalCode = c.Code, IsActive = true, IsSystem = true,
            LabelJson = $"{{\"es\":\"es {c.Code}\",\"en\":\"en {c.Code}\"}}",
            ExtraJson = $"{{\"module\":\"WAREHOUSE\",\"mandatory\":{(c.Mandatory ? "true" : "false")},\"defaultOn\":{(c.DefaultOn || (binMovedOn && c.Code == ActivityEvents.BinMoved) ? "true" : "false")}}}",
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
    public async Task Feed_labels_follow_the_user_language()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var r = await f.AddReceiptAsync(w1, ReceiptStatuses.Received);
        f.Db.Set<Teikem.Domain.Wms.ReceiptLine>().Add(new Teikem.Domain.Wms.ReceiptLine
        {
            ReceiptHeaderId = r.ReceiptHeaderId, ProductId = (await f.AddProductAsync("SKU-A")).ProductId, ReceivedQty = 2m,
        });
        History(f, EntityTypes.Receipt, r.ReceiptHeaderId, StatusDomains.ReceiptStatus, ReceiptStatuses.Received, DateTime.UtcNow.AddMinutes(-5));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        f.Tenant.Lang = "en";
        var page = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse), default);
        var confirmed = Assert.Single(page.Items, i => i.Code == ActivityEvents.ReceiptConfirmed);
        Assert.Equal("en " + ActivityEvents.ReceiptConfirmed, confirmed.Label);
        Assert.Contains("1 line", confirmed.Detail);

        f.Tenant.Lang = "es";
        var es = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse), default);
        var confirmedEs = Assert.Single(es.Items, i => i.Code == ActivityEvents.ReceiptConfirmed);
        Assert.Equal("es " + ActivityEvents.ReceiptConfirmed, confirmedEs.Label);
        Assert.Contains("1 línea", confirmedEs.Detail);
    }

    /// <summary>
    /// Maestro L54: el LookupCodeOverride del tenant renombra y deshabilita eventos del catálogo también en el feed. Un
    /// obligatorio deshabilitado se sigue mostrando (no se degrada); el override de otro tenant no aplica.
    /// </summary>
    [Fact]
    public async Task Feed_applies_the_tenant_catalog_override()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var r = await f.AddReceiptAsync(w1, ReceiptStatuses.Received);
        var po = await AddPurchaseOrderAsync(f, w1, "PO-00077");
        var at = DateTime.UtcNow.AddMinutes(-5);
        History(f, EntityTypes.Receipt, r.ReceiptHeaderId, StatusDomains.ReceiptStatus, ReceiptStatuses.Received, at);
        History(f, EntityTypes.PurchaseOrder, po.PurchaseOrderId, StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Sent, at);
        History(f, EntityTypes.PurchaseOrder, po.PurchaseOrderId, StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Cancelled, at);
        int CatalogId(string code) => LookupIdOf(f, ActivityRules.CatalogDomain, code);
        f.Db.LookupCodeOverrides.AddRange(
            new LookupCodeOverride { TenantId = f.Tenant.TenantId!.Value, LookupCodeId = CatalogId(ActivityEvents.ReceiptConfirmed), CustomLabelJson = "{\"es\":\"Recepción cerrada\"}" },
            new LookupCodeOverride { TenantId = f.Tenant.TenantId!.Value, LookupCodeId = CatalogId(ActivityEvents.PoSent), IsEnabled = false },
            new LookupCodeOverride { TenantId = f.Tenant.TenantId!.Value, LookupCodeId = CatalogId(ActivityEvents.PoCancelled), IsEnabled = false,
                CustomExtraJson = "{\"mandatory\":false}" },
            // Otro tenant renombra el mismo evento: no debe verse aquí.
            new LookupCodeOverride { TenantId = WmsFixture.OtherTenantId, LookupCodeId = CatalogId(ActivityEvents.ReceiptConfirmed), CustomLabelJson = "{\"es\":\"Ajeno\"}" });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var page = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse), default);
        var confirmed = Assert.Single(page.Items, i => i.Code == ActivityEvents.ReceiptConfirmed);
        Assert.Equal("Recepción cerrada", confirmed.Label);
        Assert.True(confirmed.Mandatory);
        Assert.DoesNotContain(page.Items, i => i.Code == ActivityEvents.PoSent);              // opcional deshabilitado
        var cancelled = Assert.Single(page.Items, i => i.Code == ActivityEvents.PoCancelled);  // obligatorio: sigue y sigue obligatorio
        Assert.True(cancelled.Mandatory);

        f.Tenant.Lang = "en";   // el override solo trae "es": en inglés queda la etiqueta base
        var en = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse), default);
        Assert.Equal("en " + ActivityEvents.ReceiptConfirmed, Assert.Single(en.Items, i => i.Code == ActivityEvents.ReceiptConfirmed).Label);
    }

    private static async Task<Teikem.Domain.Wms.PurchaseOrder> AddPurchaseOrderAsync(WmsFixture f, Teikem.Domain.Wms.Warehouse w, string number)
    {
        var po = new Teikem.Domain.Wms.PurchaseOrder
        {
            PublicId = Guid.NewGuid(), TenantId = f.Tenant.TenantId!.Value, SupplierId = 1, WarehouseId = w.WarehouseId, Number = number,
            OrderDate = Teikem.Infrastructure.Abstractions.TenantClock.Default.Today, StatusCodeId = f.StatusId(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Sent),
            IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<Teikem.Domain.Wms.PurchaseOrder>().Add(po);
        await f.Db.SaveChangesAsync();
        return po;
    }

    /// <summary>
    /// Las fuentes del proveedor (StatusSources) incluyen RECEIPT → PUTAWAY y PURCHASE_ORDER → RECEIVED / CANCELLED: quitar
    /// cualquiera de esos estatus rompe esta prueba (PO_CANCELLED es obligatorio y también sale con 'solo obligatorios').
    /// </summary>
    [Fact]
    public async Task Warehouse_provider_reads_receipt_putaway_and_purchase_order_received_and_cancelled()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var r = await f.AddReceiptAsync(w1, ReceiptStatuses.Putaway);
        var received = await AddPurchaseOrderAsync(f, w1, "PO-00101");
        var cancelled = await AddPurchaseOrderAsync(f, w1, "PO-00102");
        var at = DateTime.UtcNow.AddMinutes(-5);
        History(f, EntityTypes.Receipt, r.ReceiptHeaderId, StatusDomains.ReceiptStatus, ReceiptStatuses.Putaway, at);
        History(f, EntityTypes.PurchaseOrder, received.PurchaseOrderId, StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Received, at);
        History(f, EntityTypes.PurchaseOrder, cancelled.PurchaseOrderId, StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Cancelled, at);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var page = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse), default);
        var putaway = Assert.Single(page.Items, i => i.Code == ActivityEvents.ReceiptPutawayDone);
        Assert.Equal((EntityTypes.Receipt, r.ReceiptHeaderId, r.Number, false), (putaway.EntityType, putaway.EntityId, putaway.Reference, putaway.Mandatory));
        var poReceived = Assert.Single(page.Items, i => i.Code == ActivityEvents.PoReceived);
        Assert.Equal((EntityTypes.PurchaseOrder, received.PurchaseOrderId, received.PublicId, "PO-00101", false),
            (poReceived.EntityType, poReceived.EntityId, poReceived.PublicId!.Value, poReceived.Reference, poReceived.Mandatory));
        var poCancelled = Assert.Single(page.Items, i => i.Code == ActivityEvents.PoCancelled);
        Assert.Equal((EntityTypes.PurchaseOrder, cancelled.PurchaseOrderId, cancelled.PublicId, "PO-00102", true),
            (poCancelled.EntityType, poCancelled.EntityId, poCancelled.PublicId!.Value, poCancelled.Reference, poCancelled.Mandatory));

        var mandatory = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse, "24h", OnlyMandatory: true), default);
        Assert.Contains(mandatory.Items, i => i.Code == ActivityEvents.PoCancelled && i.Reference == "PO-00102");
        Assert.DoesNotContain(mandatory.Items, i => i.Code is ActivityEvents.PoReceived or ActivityEvents.ReceiptPutawayDone);
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
            OrderDate = Teikem.Infrastructure.Abstractions.TenantClock.Default.Today, StatusCodeId = f.StatusId(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Partial),
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
        var security = new RecordingSecurityEventWriter();
        await using var f = await CreateAsync(security: security);
        var feed = f.Get<ActivityFeedService>();

        f.SetPermissions(PermissionCatalog.AnalyticsView);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => feed.GetAsync(new ActivityQuery("warehouse"), default));
        Assert.Equal("No tiene permiso para ver la actividad del módulo WAREHOUSE.", ex.Message);
        // El 403 queda en la bitácora de seguridad (PERMISSION_DENIED, BLOCKED).
        Assert.Equal((SecurityEventTypes.PermissionDenied, SecurityOutcomes.Blocked), Assert.Single(security.Calls));
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
    public async Task Warehouse_provider_maps_done_and_cancelled_tasks_by_type()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var p = await f.AddProductAsync("SKU-A");
        var putaway = await f.AddTaskAsync(new WarehouseTaskSpec(WarehouseTaskTypes.Putaway, w1.WarehouseId, p.ProductId, 3m));
        var replenish = await f.AddTaskAsync(new WarehouseTaskSpec(WarehouseTaskTypes.Replenish, w1.WarehouseId, p.ProductId, 2m));
        var cancelled = await f.AddTaskAsync(new WarehouseTaskSpec(WarehouseTaskTypes.Putaway, w1.WarehouseId, p.ProductId, 1m));
        var count = await f.AddTaskAsync(new WarehouseTaskSpec(WarehouseTaskTypes.Count, w1.WarehouseId, p.ProductId, 1m));   // control
        var at = DateTime.UtcNow.AddMinutes(-5);
        History(f, EntityTypes.WarehouseTask, putaway.WarehouseTaskId, StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Done, at);
        History(f, EntityTypes.WarehouseTask, replenish.WarehouseTaskId, StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Done, at);
        History(f, EntityTypes.WarehouseTask, cancelled.WarehouseTaskId, StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Cancelled, at);
        History(f, EntityTypes.WarehouseTask, count.WarehouseTaskId, StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Done, at);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var page = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse), default);
        var done = Assert.Single(page.Items, i => i.Code == ActivityEvents.PutawayDone);
        Assert.Equal((EntityTypes.WarehouseTask, putaway.WarehouseTaskId, $"#{putaway.WarehouseTaskId}"), (done.EntityType, done.EntityId, done.Reference));
        Assert.Contains("W1", done.Detail);
        Assert.Contains("SKU-A", done.Detail);
        var replenished = Assert.Single(page.Items, i => i.Code == ActivityEvents.ReplenishDone);
        Assert.Equal((EntityTypes.WarehouseTask, replenish.WarehouseTaskId), (replenished.EntityType, replenished.EntityId));
        var taskCancelled = Assert.Single(page.Items, i => i.Code == ActivityEvents.TaskCancelled);
        Assert.Equal((EntityTypes.WarehouseTask, cancelled.WarehouseTaskId), (taskCancelled.EntityType, taskCancelled.EntityId));
        Assert.DoesNotContain(page.Items, i => i.EntityId == count.WarehouseTaskId && i.EntityType == EntityTypes.WarehouseTask);   // COUNT DONE: sin evento
        Assert.Equal(3, page.Items.Count(i => i.EntityType == EntityTypes.WarehouseTask));
    }

    [Fact]
    public async Task Warehouse_provider_reads_asn_count_pick_and_warehouse_history()
    {
        await using var f = await CreateAsync();
        var tenantId = f.Tenant.TenantId!.Value;
        var w1 = await f.AddWarehouseAsync("W1");
        var w9 = await f.AddWarehouseAsync("W9");
        var b1 = await f.AddBinAsync(await f.AddZoneAsync(w1, "PCK", ZoneTypes.Picking), "P-01");
        var client = await f.AddClientAsync("CLI");
        var pa = await f.AddProductAsync("SKU-A");
        var pb = await f.AddProductAsync("SKU-B");

        var asn = new Teikem.Domain.Wms.Asn
        {
            TenantId = tenantId, WarehouseId = w1.WarehouseId, ClientId = client.ClientId, Reference = null,
            StatusCodeId = f.StatusId(StatusDomains.AsnStatus, AsnStatuses.Cancelled), CreatedAtUtc = DateTime.UtcNow,
        };
        var cc = new Teikem.Domain.Wms.CycleCount
        {
            TenantId = tenantId, WarehouseId = w1.WarehouseId, Number = "CC-00088",
            StatusCodeId = f.StatusId(StatusDomains.CycleCountStatus, CycleCountStatuses.ReconciledVariance), CreatedAtUtc = DateTime.UtcNow,   // Lote 14: 'Diferencia'
        };
        var batch = new Teikem.Domain.Wms.PickBatch
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = w1.WarehouseId, Number = "PB-00007", ClientInvoiceNumber = "FAC-77",
            StatusCodeId = f.StatusId(StatusDomains.PickBatchStatus, PickBatchStatuses.Cancelled), CollectedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<Teikem.Domain.Wms.Asn>().Add(asn);
        f.Db.Set<Teikem.Domain.Wms.CycleCount>().Add(cc);
        f.Db.Set<Teikem.Domain.Wms.PickBatch>().Add(batch);
        await f.Db.SaveChangesAsync();
        f.Db.Set<Teikem.Domain.Wms.AsnLine>().AddRange(
            new Teikem.Domain.Wms.AsnLine { AsnId = asn.AsnId, ProductId = pa.ProductId, ExpectedQty = 5m },
            new Teikem.Domain.Wms.AsnLine { AsnId = asn.AsnId, ProductId = pb.ProductId, ExpectedQty = 3m });
        // Línea 1: la foto decía 5, el saldo al reconciliar era 6 y se contaron 7 → +1 (no +2). Línea 2 cuadra.
        f.Db.Set<Teikem.Domain.Wms.CycleCountLine>().AddRange(
            new Teikem.Domain.Wms.CycleCountLine { CycleCountId = cc.CycleCountId, WarehouseBinId = b1.WarehouseBinId, ProductId = pa.ProductId, SystemQty = 5m, CountedQty = 7m, ReconciledSystemQty = 6m },
            new Teikem.Domain.Wms.CycleCountLine { CycleCountId = cc.CycleCountId, WarehouseBinId = b1.WarehouseBinId, ProductId = pb.ProductId, SystemQty = 3m, CountedQty = 3m, ReconciledSystemQty = 3m });
        f.Db.Set<Teikem.Domain.Wms.PickBatchLine>().AddRange(
            new Teikem.Domain.Wms.PickBatchLine { PickBatchId = batch.PickBatchId, ProductId = pa.ProductId, FromBinId = b1.WarehouseBinId, Quantity = 1m, IssueTxnId = 1 },
            new Teikem.Domain.Wms.PickBatchLine { PickBatchId = batch.PickBatchId, ProductId = pb.ProductId, FromBinId = b1.WarehouseBinId, Quantity = 2m, IssueTxnId = 2 });
        var at = DateTime.UtcNow.AddMinutes(-5);
        History(f, EntityTypes.Asn, asn.AsnId, StatusDomains.AsnStatus, AsnStatuses.Cancelled, at);
        History(f, EntityTypes.CycleCount, cc.CycleCountId, StatusDomains.CycleCountStatus, CycleCountStatuses.Counted, at.AddMinutes(-1));
        History(f, EntityTypes.CycleCount, cc.CycleCountId, StatusDomains.CycleCountStatus, CycleCountStatuses.ReconciledVariance, at);   // Lote 14 (D7)
        History(f, EntityTypes.PickBatch, batch.PickBatchId, StatusDomains.PickBatchStatus, PickBatchStatuses.Collected, at.AddMinutes(-2));
        History(f, EntityTypes.PickBatch, batch.PickBatchId, StatusDomains.PickBatchStatus, PickBatchStatuses.Packed, at.AddMinutes(-1));
        History(f, EntityTypes.PickBatch, batch.PickBatchId, StatusDomains.PickBatchStatus, PickBatchStatuses.Cancelled, at);
        History(f, EntityTypes.Warehouse, w9.WarehouseId, StatusDomains.WarehouseStatus, WarehouseStatuses.Inactive, at);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var page = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse), default);

        var asnCancelled = Assert.Single(page.Items, i => i.Code == ActivityEvents.AsnCancelled);
        Assert.Equal((EntityTypes.Asn, asn.AsnId, (Guid?)null, $"ASN #{asn.AsnId}", false),
            (asnCancelled.EntityType, asnCancelled.EntityId, asnCancelled.PublicId, asnCancelled.Reference, asnCancelled.Mandatory));
        Assert.Contains("W1", asnCancelled.Detail);
        Assert.Contains("2 líneas", asnCancelled.Detail);

        var finished = Assert.Single(page.Items, i => i.Code == ActivityEvents.CountFinished);
        Assert.Equal((EntityTypes.CycleCount, cc.CycleCountId, (Guid?)null, "CC-00088", false),
            (finished.EntityType, finished.EntityId, finished.PublicId, finished.Reference, finished.Mandatory));
        Assert.Contains("2 líneas", finished.Detail);
        Assert.DoesNotContain("Diferencia neta", finished.Detail);
        var reconciled = Assert.Single(page.Items, i => i.Code == ActivityEvents.CountReconciled);
        Assert.Equal((EntityTypes.CycleCount, cc.CycleCountId, (Guid?)null, "CC-00088", true),
            (reconciled.EntityType, reconciled.EntityId, reconciled.PublicId, reconciled.Reference, reconciled.Mandatory));
        Assert.Contains("W1", reconciled.Detail);
        Assert.Contains("Diferencia neta +1", reconciled.Detail);   // contra ReconciledSystemQty, no contra la foto

        foreach (var code in new[] { ActivityEvents.PickCollected, ActivityEvents.PickPacked, ActivityEvents.PickCancelled })
        {
            var e = Assert.Single(page.Items, i => i.Code == code);
            Assert.Equal((EntityTypes.PickBatch, batch.PickBatchId, (Guid?)batch.PublicId, "PB-00007"), (e.EntityType, e.EntityId, e.PublicId, e.Reference));
            Assert.Contains("W1", e.Detail);
            Assert.Contains("FAC-77", e.Detail);
            Assert.Contains("2 líneas", e.Detail);
        }

        var deactivated = Assert.Single(page.Items, i => i.Code == ActivityEvents.WarehouseDeactivated);
        Assert.Equal((EntityTypes.Warehouse, w9.WarehouseId, (Guid?)w9.PublicId, "W9", true),
            (deactivated.EntityType, deactivated.EntityId, deactivated.PublicId, deactivated.Reference, deactivated.Mandatory));

        var mandatory = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse, "24h", OnlyMandatory: true), default);
        Assert.Equal(new[] { ActivityEvents.CountReconciled, ActivityEvents.PickCancelled, ActivityEvents.WarehouseDeactivated },
            mandatory.Items.Select(i => i.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Transfers_with_a_source_document_count_only_when_they_cross_warehouses()
    {
        await using var f = await CreateAsync(binMovedOn: true);
        var w1 = await f.AddWarehouseAsync("W1");
        var w2 = await f.AddWarehouseAsync("W2");
        var b1 = await f.AddBinAsync(await f.AddZoneAsync(w1, "PCK", ZoneTypes.Picking), "P-01");
        var b1b = await f.AddBinAsync(await f.AddZoneAsync(w1, "RSV", ZoneTypes.Reserve), "R-01");
        var b2 = await f.AddBinAsync(await f.AddZoneAsync(w2, "PCK", ZoneTypes.Picking), "P-02");
        var p = await f.AddProductAsync("SKU-A");
        await f.PostAsync(
            new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 10m, ToWarehouseId: w1.WarehouseId, ToBinId: b1.WarehouseBinId),
            new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 10m, ToWarehouseId: w2.WarehouseId, ToBinId: b2.WarehouseBinId));

        // Conciliación por serie de un conteo: la serie estaba en W2 y se contó en W1 → cruza de almacén.
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, p.ProductId, 1m, FromWarehouseId: w2.WarehouseId, FromBinId: b2.WarehouseBinId,
            ToWarehouseId: w1.WarehouseId, ToBinId: b1.WarehouseBinId, RefEntityType: EntityTypes.CycleCount, RefId: 77));
        // Con documento y dentro del mismo almacén: ya es COUNT_RECONCILED / PUTAWAY_DONE / REPLENISH_DONE.
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, p.ProductId, 2m, FromWarehouseId: w1.WarehouseId, FromBinId: b1.WarehouseBinId,
            ToWarehouseId: w1.WarehouseId, ToBinId: b1b.WarehouseBinId, RefEntityType: EntityTypes.CycleCount, RefId: 77));
        // Manual dentro del mismo almacén: BIN_MOVED (encendido en esta prueba).
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, p.ProductId, 3m, FromWarehouseId: w1.WarehouseId, FromBinId: b1.WarehouseBinId,
            ToWarehouseId: w1.WarehouseId, ToBinId: b1b.WarehouseBinId));

        var page = await f.Get<ActivityFeedService>().GetAsync(new ActivityQuery(BusinessModules.Warehouse), default);
        var transferred = Assert.Single(page.Items, i => i.Code == ActivityEvents.InventoryTransferred);
        Assert.Contains("W2 → W1", transferred.Detail);
        var moved = Assert.Single(page.Items, i => i.Code == ActivityEvents.BinMoved);
        Assert.Contains("P-01 → R-01", moved.Detail);
        Assert.EndsWith("· 3", moved.Detail);   // la de 2 (con documento, mismo almacén) no aparece
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
