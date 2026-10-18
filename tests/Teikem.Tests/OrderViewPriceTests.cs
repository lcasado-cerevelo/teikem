using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Teikem.Api.Controllers;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Seeding;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 39 (A) — permiso orders.view_price («Ver precios de las órdenes»). Una sola decisión (OrderPriceVisibility) y cada
/// superficie con y sin el permiso: lista, lookup y ficha (QuotedAmount null; el COD sigue visible), GET /quote (403), 422
/// credit_exceeded (sin cifras, mismo código), comentario del override en el historial, error de crédito de una fila de
/// importación y campo QuotedAmount de la fuente de datos de Análisis. Más el catálogo, las plantillas (TenantAdmin y Billing) y
/// la sección «2026-10-12 (a)» de logistica-db-update.sql que lo propaga a los roles ya clonados.
/// </summary>
public sealed class OrderViewPriceTests
{
    private const string ViewPrice = PermissionCatalog.OrdersViewPrice;

    // ================================================================ catálogo, plantillas y SQL

    [Fact]
    public void Permission_is_in_the_catalog_with_its_label_and_category()
    {
        Assert.Equal("orders.view_price", ViewPrice);
        var p = Assert.Single(PermissionCatalog.All, x => x.Code == ViewPrice);
        Assert.Equal(("ORDERS", "Ver precios de las órdenes", "View order prices"), (p.Category, p.LabelEs, p.LabelEn));
    }

    [Fact]
    public void Only_TenantAdmin_and_Billing_templates_bring_it()
    {
        var withIt = PermissionCatalog.RoleTemplates.Where(t => t.Value.Contains(ViewPrice)).Select(t => t.Key).OrderBy(k => k).ToArray();
        Assert.Equal(new[] { "Billing", "TenantAdmin" }, withIt);
        var newCodes = new HashSet<string> { ViewPrice };
        Assert.Equal(new[] { ViewPrice }, PermissionCatalog.CodesToPropagate("Billing", newCodes, new HashSet<string>()));
        Assert.Equal(new[] { ViewPrice }, PermissionCatalog.CodesToPropagate("TenantAdmin", newCodes, new HashSet<string>()));
        foreach (var role in new[] { "Dispatcher", "WarehouseOperator", "Driver", "ReadOnly", "Rol propio" })
            Assert.Empty(PermissionCatalog.CodesToPropagate(role, newCodes, new HashSet<string>()));
        Assert.Empty(PermissionCatalog.CodesToPropagate("Billing", newCodes, newCodes));   // ya lo tiene: no se toca
    }

    private static string UpdateSql()
        => File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

    [Fact]
    public void Update_sql_adds_the_permission_and_propagates_it_to_templates_and_cloned_roles_in_a_new_section_at_the_end()
    {
        var sql = UpdateSql();
        var start = sql.IndexOf("2026-10-12 (a) — Ver precios de las órdenes (Lote 39): permiso orders.view_price.", StringComparison.Ordinal);
        Assert.True(start > 0, "Falta la sección 2026-10-12 (a) en logistica-db-update.sql.");
        // Sección nueva AL FINAL: después de la última publicada (2026-10-11 (g)).
        Assert.True(start > sql.IndexOf("2026-10-11 (g) — Motivo de despacho manual", StringComparison.Ordinal));
        var section = UpdateSqlSection.From(sql, start);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'orders.view_price')", section);
        Assert.Contains("(SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'ORDERS')", section);
        Assert.Contains("N'{\"es\":\"Ver precios de las órdenes\",\"en\":\"View order prices\"}'", section);
        // Plantillas (TenantId NULL) Y roles ya clonados de cada compañía (sin filtrar por TenantId), solo agregando.
        Assert.Contains("JOIN dbo.Permission p ON p.Code = 'orders.view_price'\nWHERE r.Name IN ('TenantAdmin', 'Billing')\n  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);", section);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dispatcher", section);
        Assert.EndsWith("GO\n", section);
    }

    [Fact]
    public async Task The_real_seeder_also_gives_it_to_cloned_TenantAdmin_and_Billing_roles_only()
    {
        var tenant = new TenantContext { TenantId = null, UserId = null };
        var options = new DbContextOptionsBuilder<Teikem.Infrastructure.Persistence.TeikemDbContext>()
            .UseInMemoryDatabase("view-price-" + Guid.NewGuid(), b => b.EnableNullChecks(false)).Options;
        await using var db = new Teikem.Infrastructure.Persistence.TeikemDbContext(options, tenant);
        var lookups = new TripTestLookups();
        var categories = PermissionCatalog.All.Select(p => p.Category).Distinct().Select((c, n) => new LookupCode
        {
            LookupCodeId = 100 + n, Entity = LookupDomains.PermissionCategory, InternalCode = c, LabelJson = "{}", IsActive = true,
        }).ToList();
        db.LookupCodes.AddRange(categories);
        lookups.Load(categories);
        var old = PermissionCatalog.All.Where(p => p.Code != ViewPrice)
            .Select(p => new Permission { Code = p.Code, CategoryLookupId = categories.First(c => c.InternalCode == p.Category).LookupCodeId, LabelJson = "{}" }).ToList();
        db.Permissions.AddRange(old);
        await db.SaveChangesAsync();
        Role Make(int? tenantId, string name) => MakeRole(tenantId, name, PermissionCatalog.RoleTemplates[name].Where(c => c != ViewPrice), old);
        foreach (var name in PermissionCatalog.RoleTemplates.Keys) db.AppRoles.Add(Make(null, name));
        foreach (var name in new[] { "TenantAdmin", "Billing", "Dispatcher", "ReadOnly" }) db.AppRoles.Add(Make(7, name));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new PermissionSeeder(db, tenant, lookups, NullLogger<PermissionSeeder>.Instance).SeedAsync(default);
        db.ChangeTracker.Clear();

        var codes = await db.Permissions.AsNoTracking().ToDictionaryAsync(p => p.PermissionId, p => p.Code);
        var roles = await db.AppRoles.IgnoreQueryFilters().AsNoTracking().Include(r => r.Permissions).ToListAsync();
        bool Has(int? tenantId, string name) => roles.Single(r => r.TenantId == tenantId && r.Name == name).Permissions.Any(rp => codes[rp.PermissionId] == ViewPrice);
        Assert.True(Has(null, "TenantAdmin"));
        Assert.True(Has(null, "Billing"));
        Assert.True(Has(7, "TenantAdmin"));
        Assert.True(Has(7, "Billing"));
        Assert.False(Has(7, "Dispatcher"));
        Assert.False(Has(7, "ReadOnly"));
        Assert.False(Has(null, "Driver"));
    }

    private static Role MakeRole(int? tenantId, string name, IEnumerable<string> codes, IReadOnlyList<Permission> perms)
    {
        var role = new Role { TenantId = tenantId, Name = name, DescriptionJson = "{}", IsSystem = tenantId is null, IsActive = true };
        foreach (var code in codes) role.Permissions.Add(new RolePermission { PermissionId = perms.Single(p => p.Code == code).PermissionId });
        return role;
    }

    // ================================================================ reglas puras

    [Fact]
    public void Credit_message_without_amounts_keeps_the_code()
    {
        var check = new CreditCheck(10m, 0m, 26.25m);
        var full = new CreditExceededException(check, 10m, showAmounts: true);
        Assert.Equal("El cliente excede su límite de crédito: límite 10.00, en curso 0.00, esta orden 26.25.", full.Message);
        Assert.Equal(new[] { "limit", "exposure", "newAmount", "available" }, full.Errors!.Keys.ToArray());
        Assert.True(full.ShowsAmounts);

        var hidden = new CreditExceededException(check, 10m, showAmounts: false);
        Assert.Equal("El cliente excede su límite de crédito.", hidden.Message);
        Assert.Equal(CreditRules.RedactedMessage, hidden.Message);
        Assert.Equal((422, "credit_exceeded"), (hidden.StatusCode, hidden.Code));
        Assert.NotNull(hidden.Errors);
        Assert.Empty(hidden.Errors!);
        Assert.False(hidden.ShowsAmounts);

        // El constructor de siempre sigue con cifras (compatibilidad).
        Assert.True(new CreditExceededException(check, 10m).ShowsAmounts);
    }

    [Fact]
    public void Credit_texts_are_recognized_and_redacted()
    {
        var full = CreditRules.Message(new CreditCheck(10m, 5m, 26.25m));
        Assert.True(CreditRules.IsCreditMessage(full));
        Assert.True(CreditRules.IsCreditMessage(CreditRules.RedactedMessage));
        Assert.False(CreditRules.IsCreditMessage("No hay tarifa vigente."));
        Assert.False(CreditRules.IsCreditMessage(null));
        Assert.Equal(CreditRules.RedactedMessage, OrderPriceRules.RedactCreditText(full));
        Assert.Equal("Otro error.", OrderPriceRules.RedactCreditText("Otro error."));
        Assert.Null(OrderPriceRules.RedactCreditText(null));
    }

    [Fact]
    public void Override_history_comment_loses_only_its_amounts()
    {
        var comment = OrderStatusService.CreditOverrideCommentPrefix + "Ana Pérez (límite 10.00, en curso 0.00, esta orden 26.25)";
        Assert.Equal(comment, OrderPriceRules.RedactHistoryComment(comment, canView: true));
        Assert.Equal("Crédito excedido autorizado por Ana Pérez", OrderPriceRules.RedactHistoryComment(comment, canView: false));
        Assert.Equal("Cliente desistió", OrderPriceRules.RedactHistoryComment("Cliente desistió", canView: false));
        Assert.Null(OrderPriceRules.RedactHistoryComment(null, canView: false));
    }

    [Fact]
    public void Import_row_redaction_touches_only_the_credit_error()
    {
        var empty = new Dictionary<string, string>();
        var credit = new ImportRowDto(1, 2, empty, null, empty, Array.Empty<string>(), true, null, null, null,
            CreditRules.Message(new CreditCheck(10m, 0m, 26.25m)), false);
        var other = credit with { Error = "Cliente no encontrado." };
        Assert.Equal(credit, OrderPriceRules.Apply(credit, canView: true));
        Assert.Equal(CreditRules.RedactedMessage, OrderPriceRules.Apply(credit, canView: false).Error);
        Assert.Equal(other, OrderPriceRules.Apply(other, canView: false));
    }

    [Fact]
    public async Task Without_the_decision_service_prices_are_hidden_fail_closed()
        => Assert.False(await OrderPriceVisibility.CanViewAsync(null, default));

    // ================================================================ lista, lookup y ficha (OrderReadService)

    [Fact]
    public async Task List_shows_quoted_amount_only_with_view_price_and_cod_always()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        var q = new OrderListQuery(null, null, null, null, null, null, null, null, null);

        f.SetPermissions(PermissionCatalog.OrdersView, ViewPrice);
        var shown = (await f.Reader.GetListAsync(q, OrderScope.Any, default)).Items.ToDictionary(i => i.Id);
        Assert.Equal(26.25m, shown[2].QuotedAmount);
        Assert.Equal(40m, shown[3].QuotedAmount);
        Assert.Null(shown[1].QuotedAmount);   // sin cotizar
        Assert.Equal(200m, shown[2].CodAmount);

        f.SetPermissions(PermissionCatalog.OrdersView);
        var hidden = (await f.Reader.GetListAsync(q, OrderScope.Any, default)).Items;
        Assert.All(hidden, i => Assert.Null(i.QuotedAmount));
        Assert.Equal(200m, hidden.Single(i => i.Id == 2).CodAmount);   // el COD no es un precio: sigue visible
        Assert.Equal(4, hidden.Count);                                   // ocultar no cambia qué filas salen
    }

    [Fact]
    public async Task Lookup_hides_the_quoted_amount_without_view_price()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        f.SetPermissions(PermissionCatalog.OrdersView, ViewPrice);
        Assert.Equal(26.25m, Assert.Single((await f.Reader.LookupAsync("EMP-2", OrderScope.Any, default)).Matches).QuotedAmount);
        f.SetPermissions(PermissionCatalog.OrdersView, PermissionCatalog.TripsScan);
        var match = Assert.Single((await f.Reader.LookupAsync("EMP-2", OrderScope.Any, default)).Matches);
        Assert.Null(match.QuotedAmount);
        Assert.Equal(200m, match.CodAmount);
    }

    [Fact]
    public async Task Detail_hides_the_quoted_amount_without_view_price()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        f.SetPermissions(PermissionCatalog.OrdersView, ViewPrice);
        var shown = await f.Reader.GetAsync(f.O2.PublicId, OrderScope.Any, default);
        Assert.Equal(26.25m, shown.QuotedAmount);

        f.SetPermissions(PermissionCatalog.OrdersView, PermissionCatalog.OrdersEdit, PermissionCatalog.OrdersCreditOverride);
        var hidden = await f.Reader.GetAsync(f.O2.PublicId, OrderScope.Any, default);
        Assert.Null(hidden.QuotedAmount);
        Assert.Equal(200m, hidden.CodAmount);
        Assert.Equal(shown with { QuotedAmount = null, Capabilities = hidden.Capabilities, Packages = hidden.Packages, References = hidden.References,
            Delivery = hidden.Delivery, Pickup = hidden.Pickup }, hidden);   // nada más cambia
    }

    [Fact]
    public async Task Platform_admin_sees_prices()
    {
        await using var f = await OrderReadFixture.CreateAsync();   // arranca como administrador de plataforma
        Assert.Equal(26.25m, (await f.Reader.GetAsync(f.O2.PublicId, OrderScope.Any, default)).QuotedAmount);
    }

    [Fact]
    public async Task A_reader_built_without_the_decision_hides_prices()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        var reader = new OrderReadService(f.Db, f.Tenant, f.Lookups, f.Statuses);
        Assert.Null((await reader.GetAsync(f.O2.PublicId, OrderScope.Any, default)).QuotedAmount);
    }

    // ================================================================ GET /orders/{id}/quote

    [Fact]
    public async Task Quote_endpoint_is_403_without_view_price()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        f.SetPermissions(PermissionCatalog.OrdersView);
        var controller = new OrderStatusController(null!, f.Permissions, f.Prices);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => controller.PreviewQuote(f.O2.PublicId, default));
        Assert.Equal("Falta el permiso 'orders.view_price'.", ex.Message);
        Assert.Equal(OrderPriceVisibility.ForbiddenMessage, ex.Message);
        Assert.Equal(403, ex.StatusCode);

        f.SetPermissions(PermissionCatalog.OrdersView, ViewPrice);
        await f.Prices.EnsureAsync(default);   // con el permiso, el guard deja pasar (el resto lo cubre el smoke)
    }

    // ================================================================ 422 credit_exceeded (efecto de confirmar)

    [Fact]
    public async Task Credit_exceeded_carries_amounts_only_with_view_price()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        var services = new ServiceCollection().AddSingleton(f.Prices).BuildServiceProvider();
        var check = new CreditCheck(10m, 0m, 26.25m);

        f.SetPermissions(PermissionCatalog.OrdersEdit, ViewPrice);
        var shown = await OrderStatusEffect.CreditExceededAsync(services, check, 10m, default);
        Assert.Equal(CreditRules.Message(check), shown.Message);
        Assert.Equal(new[] { "10.00" }, shown.Errors!["limit"]);

        f.SetPermissions(PermissionCatalog.OrdersEdit);
        var hidden = await OrderStatusEffect.CreditExceededAsync(services, check, 10m, default);
        Assert.Equal("El cliente excede su límite de crédito.", hidden.Message);
        Assert.Equal("credit_exceeded", hidden.Code);
        Assert.Empty(hidden.Errors!);

        // Sin el servicio de la decisión en el scope: sin cifras.
        var bare = await OrderStatusEffect.CreditExceededAsync(new ServiceCollection().BuildServiceProvider(), check, 10m, default);
        Assert.False(bare.ShowsAmounts);
    }

    // ================================================================ historial de estatus de la orden

    [Fact]
    public async Task Order_history_hides_the_override_amounts_without_view_price()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        var typeId = await f.Lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder);
        var full = OrderStatusService.CreditOverrideCommentPrefix + "Ana (límite 10.00, en curso 0.00, esta orden 26.25)";
        f.Db.EntityStatusHistories.AddRange(
            new EntityStatusHistory { EntityStatusHistoryId = 1, TenantId = OrderReadFixture.TenantId, EntityTypeLookupId = typeId, EntityId = 2,
                ToStatusCodeId = f.StatusId(StatusDomains.OrderStatus, OrderStatuses.Submitted) },
            new EntityStatusHistory { EntityStatusHistoryId = 2, TenantId = OrderReadFixture.TenantId, EntityTypeLookupId = typeId, EntityId = 2,
                FromStatusCodeId = f.StatusId(StatusDomains.OrderStatus, OrderStatuses.Submitted),
                ToStatusCodeId = f.StatusId(StatusDomains.OrderStatus, OrderStatuses.Pickup), Comment = full });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        f.SetPermissions(PermissionCatalog.OrdersView, ViewPrice);
        Assert.Equal(full, (await f.Statuses.GetHistoryAsync(EntityTypes.TransportOrder, 2, default))[1].Comment);
        f.SetPermissions(PermissionCatalog.OrdersView);
        var rows = await f.Statuses.GetHistoryAsync(EntityTypes.TransportOrder, 2, default);
        Assert.Equal("Crédito excedido autorizado por Ana", rows[1].Comment);
        Assert.Null(rows[0].Comment);
    }

    // ================================================================ importación de órdenes

    [Fact]
    public async Task Import_batch_hides_the_credit_amounts_of_a_failed_row_without_view_price()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        var creditError = CreditRules.Message(new CreditCheck(10m, 0m, 26.25m));
        var rows = JsonSerializer.Serialize(new object[]
        {
            new { row = 1, line = 2, values = new { }, errors = new { }, warnings = Array.Empty<string>(), error = creditError },
            new { row = 2, line = 3, values = new { }, errors = new { }, warnings = Array.Empty<string>(), error = "Cliente no encontrado." },
        });
        var batch = new ImportBatch
        {
            ImportBatchId = 1, PublicId = Guid.NewGuid(), TenantId = OrderReadFixture.TenantId, Kind = ImportKinds.Order, ImportTemplateId = 1,
            ClientId = f.C1.ClientId, RowCount = 2, ValidRows = 2, StatusCodeId = f.StatusId(ImportBatchStatuses.Domain, ImportBatchStatuses.Confirmed),
            RowsJson = rows, CreatedBy = 1, ConfirmedAtUtc = DateTime.UtcNow,
        };
        f.Db.ImportBatches.Add(batch);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var imports = new OrderImportService(f.Db, f.Tenant, f.Lookups, f.Statuses, f.Permissions, null!, null!, null!, null!, null!, f.Prices);

        f.SetPermissions(PermissionCatalog.OrdersView, ViewPrice);
        var shown = await imports.GetAsync(batch.PublicId, OrderScope.Any, default);
        Assert.Equal(creditError, shown.Rows[0].Error);

        f.SetPermissions(PermissionCatalog.OrdersView);
        var hidden = await imports.GetAsync(batch.PublicId, OrderScope.Any, default);
        Assert.Equal("El cliente excede su límite de crédito.", hidden.Rows[0].Error);
        Assert.Equal("Cliente no encontrado.", hidden.Rows[1].Error);
        Assert.Equal(2, hidden.Failed);
    }

    // ================================================================ Análisis (fuente de datos TRANSPORT_ORDER)

    [Fact]
    public async Task Order_data_source_hides_the_quoted_amount_without_view_price()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        var source = new TransportOrderDataSource(f.Db, f.Lookups, f.Tenant, f.Prices);

        f.SetPermissions(PermissionCatalog.AnalyticsView, PermissionCatalog.OrdersView, ViewPrice);
        var shown = (await source.LoadAsync(new DataQuery(), default)).ToDictionary(r => (int)r["Id"]!);
        Assert.Equal(26.25m, shown[2]["QuotedAmount"]);
        Assert.Equal(200m, shown[2]["CodAmount"]);

        f.SetPermissions(PermissionCatalog.AnalyticsView, PermissionCatalog.OrdersView);
        var hidden = (await source.LoadAsync(new DataQuery(), default)).ToDictionary(r => (int)r["Id"]!);
        Assert.All(hidden.Values, r => Assert.Null(r["QuotedAmount"]));
        Assert.Equal(200m, hidden[2]["CodAmount"]);
        Assert.Contains(source.Fields, x => x.Key == "QuotedAmount");   // el campo sigue en el contrato (sin romper vistas guardadas)

        // Construida sin la decisión (como en pruebas antiguas): falla cerrado.
        var bare = (await new TransportOrderDataSource(f.Db, f.Lookups, f.Tenant).LoadAsync(new DataQuery(), default)).ToDictionary(r => (int)r["Id"]!);
        Assert.Null(bare[2]["QuotedAmount"]);
    }
}
