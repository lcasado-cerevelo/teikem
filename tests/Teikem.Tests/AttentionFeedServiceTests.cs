using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 14 (P3, D6) — "Necesita tu atención": AttentionFeedService con el proveedor real de descuadres sobre InMemory
/// (WmsFixture: PermissionService y ModuleService reales leídos de la caché). Cubre: los 5 descuadres abiertos más antiguos con
/// lo necesario para "Revisar" y el total para "Ver todos (N)"; cerrados y de otro tenant no cuentan; sin inventory.view o con
/// WMS_LOTSERIAL apagado el proveedor no aporta; vacío = total 0 y listas vacías; orden entre proveedores; el controlador exige
/// pulse.attention sin módulo; y el espejo en el seed (permiso, plantillas y propagación de una sola vez).
/// </summary>
public class AttentionFeedServiceTests
{
    private static readonly Lazy<string> Seed = new(() => File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-seed.sql")));

    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin B, List<Product> Products);

    private static async Task<World> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<IAttentionItemProvider, InventoryDiscrepancyAttentionProvider>();
            s.AddSingleton<AttentionFeedService>();
        });
        var w = await f.AddWarehouseAsync("ALM-01");
        var b = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "A-01");
        var products = new List<Product>();
        for (var i = 1; i <= 7; i++) products.Add(await f.AddProductAsync($"SKU-{i}"));
        return new World(f, w, b, products);
    }

    /// <summary>Descuadre escrito directamente (el servicio de conciliación ya lo prueba InventoryReconciliationServiceTests).</summary>
    private static async Task<InventoryDiscrepancy> AddDiscrepancyAsync(World w, Product p, DateTime detected, bool closed = false,
        int? tenantId = null, bool productTotal = false)
    {
        var f = w.F;
        var d = new InventoryDiscrepancy
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId ?? WmsFixture.TenantId,
            KindLookupId = f.LookupId(LookupDomains.InventoryDiscrepancyKind, productTotal ? DiscrepancyKinds.ProductTotal : DiscrepancyKinds.Balance),
            TriggerLookupId = f.LookupId(LookupDomains.ReconciliationTrigger, ReconciliationTriggers.Event),
            ProductId = p.ProductId, WarehouseId = productTotal ? null : w.W.WarehouseId, WarehouseBinId = productTotal ? null : w.B.WarehouseBinId,
            LedgerQty = 4m, BalanceQty = 5.5m, DetectedAtUtc = detected, LastCheckedAtUtc = detected, CheckCount = 2,
            StatusCodeId = f.StatusId(StatusDomains.InventoryDiscrepancyStatus, closed ? InventoryDiscrepancyStatuses.Dismissed : InventoryDiscrepancyStatuses.Open),
            ClosedAtUtc = closed ? detected.AddHours(1) : null,
        };
        f.Db.InventoryDiscrepancies.Add(d);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return d;
    }

    private static Task<AttentionDto> GetAsync(World w) => w.F.Get<AttentionFeedService>().GetAsync(default);

    [Fact]
    public async Task Shows_the_five_oldest_open_discrepancies_with_review_link_and_the_total()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var day = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var created = new List<InventoryDiscrepancy>();
        for (var i = 6; i >= 0; i--) created.Add(await AddDiscrepancyAsync(w, w.Products[i], day.AddDays(i), productTotal: i == 1));
        await AddDiscrepancyAsync(w, w.Products[0], day.AddDays(-5), closed: true);   // cerrado: no cuenta
        var other = await w.F.AddProductAsync("SKU-OTRO", tenantId: WmsFixture.OtherTenantId);
        await AddDiscrepancyAsync(w, other, day.AddDays(-9), tenantId: WmsFixture.OtherTenantId);   // otro tenant: no cuenta

        var dto = await GetAsync(w);
        Assert.Equal(7, dto.Total);
        Assert.Equal(AttentionFeedService.MaxItems, dto.Items.Count);
        Assert.Equal(new[] { "SKU-1", "SKU-2", "SKU-3", "SKU-4", "SKU-5" }, dto.Items.Select(i => i.Params["sku"]));
        var group = Assert.Single(dto.Groups);
        Assert.Equal((EntityTypes.InventoryDiscrepancy, BusinessModules.Warehouse, 7, "/warehouse/kardex"), (group.Code, group.Module, group.Total, group.Route));
        Assert.Equal(new Dictionary<string, string> { ["tab"] = "reconciliation", ["status"] = "OPEN" }, group.Query);

        var first = dto.Items[0];
        var oldest = created.Single(d => d.ProductId == w.Products[0].ProductId && d.ClosedAtUtc == null);
        Assert.Equal((EntityTypes.InventoryDiscrepancy, BusinessModules.Warehouse, "danger", 1, "/warehouse/kardex", day),
            (first.Code, first.Module, first.Tone, first.Count, first.Route, first.SinceUtc!.Value));
        Assert.Equal(new Dictionary<string, string> { ["tab"] = "reconciliation", ["discrepancy"] = oldest.PublicId.ToString() }, first.Query);
        Assert.Equal(oldest.PublicId.ToString(), first.Params["publicId"]);
        Assert.Equal(("BALANCE", "Producto SKU-1", "ALM-01", "A-01", "A-01", ""), (first.Params["kind"], first.Params["productName"],
            first.Params["warehouse"], first.Params["bin"], first.Params["where"], first.Params["lot"]));
        Assert.Equal(("4", "5.5", "1.5", "2026-09-01T12:00:00Z", "2"), (first.Params["ledgerQty"], first.Params["balanceQty"],
            first.Params["difference"], first.Params["detectedAtUtc"], first.Params["checkCount"]));
        // Total del producto: sin almacén ni posición.
        var total = dto.Items[1];
        Assert.Equal(("PRODUCT_TOTAL", "", "", ""), (total.Params["kind"], total.Params["warehouse"], total.Params["bin"], total.Params["where"]));
    }

    [Fact]
    public async Task Nothing_pending_is_an_empty_panel()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await AddDiscrepancyAsync(w, w.Products[0], DateTime.UtcNow, closed: true);
        var dto = await GetAsync(w);
        Assert.Equal(0, dto.Total);
        Assert.Empty(dto.Items);
        Assert.Empty(dto.Groups);
    }

    [Fact]
    public async Task Without_inventory_view_or_with_the_module_off_discrepancies_do_not_count()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await AddDiscrepancyAsync(w, w.Products[0], DateTime.UtcNow);

        w.F.SetPermissions(PermissionCatalog.PulseAttention);
        var none = await GetAsync(w);
        Assert.Equal((0, 0, 0), (none.Total, none.Items.Count, none.Groups.Count));

        w.F.SetPermissions(PermissionCatalog.PulseAttention, PermissionCatalog.InventoryView);
        Assert.Equal(1, (await GetAsync(w)).Total);

        w.F.SetModules(ModuleKeys.Analytics);
        Assert.Equal(0, (await GetAsync(w)).Total);
    }

    [Fact]
    public void Items_are_the_oldest_first_across_providers()
    {
        AttentionItemDto Item(string code, DateTime? since) => new(code, BusinessModules.Warehouse, "danger", 1,
            new Dictionary<string, string>(), null, null, since);
        var d = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var items = new[] { Item("B", d.AddDays(2)), Item("A", null), Item("C", d), Item("A", d.AddDays(2)), Item("D", d.AddDays(1)) };
        Assert.Equal(new[] { "C", "D", "A", "B" }, AttentionFeedService.Oldest(items, 4).Select(i => i.Code));
        Assert.Equal("A", AttentionFeedService.Oldest(items, 10).Last().Code);   // sin fecha, al final
        Assert.Empty(AttentionFeedService.Oldest(items, 0));
    }

    [Fact]
    public void The_endpoint_requires_pulse_attention_without_a_module()
    {
        var t = typeof(AttentionController);
        Assert.Equal(new[] { RequirePermissionAttribute.Prefix + PermissionCatalog.PulseAttention },
            t.GetCustomAttributes<RequirePermissionAttribute>(inherit: true).Select(a => a.Policy));
        Assert.Empty(t.GetCustomAttributes<RequireModuleAttribute>(inherit: true));
        Assert.Contains(t.GetCustomAttributes<AuthorizeAttribute>(inherit: true), a => a.GetType() == typeof(AuthorizeAttribute));
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("api/v1/analytics/attention", t.GetCustomAttribute<RouteAttribute>()!.Template);
        var get = Assert.Single(t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.NotNull(get.GetCustomAttribute<HttpGetAttribute>());
        Assert.Empty(get.GetCustomAttributes<RequirePermissionAttribute>());
        Assert.DoesNotContain(get.GetParameters(), p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_seed_mirrors_the_permission_the_templates_and_the_one_time_propagation()
    {
        var seed = Seed.Value.Replace("\r\n", "\n");
        var perm = Assert.Single(PermissionCatalog.All, p => p.Code == PermissionCatalog.PulseAttention);
        Assert.Equal("PULSE", perm.Category);
        Assert.Contains($"('pulse.attention','PULSE','{perm.LabelEs}','{perm.LabelEn}')", seed);
        Assert.DoesNotContain("\"", perm.LabelEs + perm.LabelEn);   // el seed arma el JSON de la etiqueta concatenando
        foreach (var role in new[] { "WarehouseOperator", "Billing", "ReadOnly" })
        {
            Assert.Contains(PermissionCatalog.PulseAttention, PermissionCatalog.RoleTemplates[role]);
            Assert.Contains($"('{role}','pulse.attention')", seed);
        }
        Assert.Contains(PermissionCatalog.PulseAttention, PermissionCatalog.RoleTemplates["TenantAdmin"]);
        Assert.DoesNotContain(PermissionCatalog.PulseAttention, PermissionCatalog.RoleTemplates["Dispatcher"]);
        Assert.DoesNotContain(PermissionCatalog.PulseAttention, PermissionCatalog.RoleTemplates["Driver"]);

        // Bloque 5b2: una sola vez (solo en la corrida que crea el permiso), a todo rol de tenant activo con inventory.view.
        Assert.Contains("SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'pulse.attention') THEN 0 ELSE 1 END AS BIT) AS IsNew\nINTO #L14AttentionIsNew;", seed);
        var block = seed[seed.IndexOf("IF EXISTS (SELECT 1 FROM #L14AttentionIsNew WHERE IsNew = 1)", StringComparison.Ordinal)..];
        block = block[..block.IndexOf("\nGO", StringComparison.Ordinal)];
        Assert.Contains("r.TenantId IS NOT NULL AND r.IsActive = 1", block);
        Assert.Contains("iv.Code = 'inventory.view'", block);
        Assert.Contains("JOIN dbo.Permission pa ON pa.Code = 'pulse.attention'", block);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM dbo.RolePermission e WHERE e.RoleId = r.RoleId AND e.PermissionId = pa.PermissionId)", block);
        // La bandera se calcula ANTES del MERGE que crea el permiso.
        Assert.True(seed.IndexOf("INTO #L14AttentionIsNew;", StringComparison.Ordinal) < seed.IndexOf("MERGE dbo.Permission AS t", StringComparison.Ordinal));
    }
}
