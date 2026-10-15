using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Teikem.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Teikem.Api.Controllers;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Tax;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Tax;
using Xunit;

namespace Teikem.Tests;

// Lote 55 (P3): impuestos. Cálculo puro (redondeo, incluido/no incluido, componentes, exentos), sección de update.sql, permiso y API por reflexión.
public class TaxTests
{
    private static TaxRateInfo Ivu(decimal state = 10.5m, decimal city = 1m)
        => new(1, "IVU", "IVU", state + city, new[] { new TaxComponentInfo("STATE", "Estatal", state), new TaxComponentInfo("CITY", "Municipal", city) });

    private static TaxLineInput L(string key, decimal amount, TaxTreatment t = TaxTreatment.Taxable) => new(key, amount, t);

    [Fact]
    public void Price_without_tax_adds_the_tax_on_top_line_by_line()
    {
        var r = TaxRules.Calculate(new[] { L("a", 10m), L("b", 20m) }, Ivu(), false, TaxRoundingModes.Line);
        Assert.Equal(1.15m, r.Lines[0].TaxAmount);
        Assert.Equal(2.30m, r.Lines[1].TaxAmount);
        Assert.Equal(30m, r.Subtotal);
        Assert.Equal(3.45m, r.TaxAmount);
        Assert.Equal(33.45m, r.Total);
    }

    [Fact]
    public void Rounding_by_line_and_by_document_can_differ_by_a_cent()
    {
        var lines = new[] { L("a", 0.33m), L("b", 0.33m), L("c", 0.33m) };
        var byLine = TaxRules.Calculate(lines, Ivu(), false, TaxRoundingModes.Line);
        var byDoc = TaxRules.Calculate(lines, Ivu(), false, TaxRoundingModes.Document);
        Assert.Equal(0.12m, byLine.TaxAmount);   // 0.04 × 3
        Assert.Equal(0.11m, byDoc.TaxAmount);    // 0.11385 redondeado una vez
        Assert.Equal(0.99m + 0.11m, byDoc.Total);
    }

    [Fact]
    public void Price_with_tax_included_extracts_the_tax_and_keeps_the_gross_total()
    {
        var r = TaxRules.Calculate(new[] { L("a", 111.5m) }, Ivu(), true, TaxRoundingModes.Line);
        Assert.Equal(11.5m, r.Lines[0].TaxAmount);
        Assert.Equal(100m, r.Lines[0].Net);
        Assert.Equal(111.5m, r.Total);
        Assert.Equal(100m, r.Subtotal);
    }

    [Fact]
    public void Components_always_add_up_to_the_tax_even_with_rounding()
    {
        var r = TaxRules.Calculate(new[] { L("a", 0.33m) }, Ivu(), false, TaxRoundingModes.Line);   // 0.04
        Assert.Equal(0.04m, r.Lines[0].TaxAmount);
        Assert.Equal(r.Lines[0].TaxAmount, r.Lines[0].Components.Sum(c => c.Amount));
        var big = TaxRules.Calculate(new[] { L("a", 1234.57m), L("b", 99.99m) }, Ivu(), false, TaxRoundingModes.Document);
        Assert.Equal(big.TaxAmount, big.Components.Sum(c => c.Amount));
        Assert.Equal(2, big.Components.Count);
    }

    [Theory]
    [InlineData(TaxTreatment.ExemptProduct, true)]
    [InlineData(TaxTreatment.ExemptClient, true)]
    [InlineData(TaxTreatment.NotTaxableCharge, false)]
    [InlineData(TaxTreatment.ZeroRate, false)]
    public void Exempt_zero_rate_and_not_taxable_lines_carry_no_tax(TaxTreatment t, bool exempt)
    {
        var r = TaxRules.Calculate(new[] { L("a", 100m, t), L("b", 100m) }, Ivu(), false, TaxRoundingModes.Line);
        Assert.Equal(0m, r.Lines[0].TaxAmount);
        Assert.Equal(exempt, r.Lines[0].IsTaxExempt);
        Assert.Null(r.Lines[0].TaxRateId);
        Assert.Equal(11.5m, r.TaxAmount);   // solo la otra línea
    }

    [Fact]
    public void Taxable_line_keeps_a_copy_of_the_rate_it_was_calculated_with()
    {
        var r = TaxRules.Calculate(new[] { L("a", 100m) }, Ivu(), false, TaxRoundingModes.Line);
        Assert.Equal(1, r.Lines[0].TaxRateId);
        Assert.Equal(11.5m, r.Lines[0].TaxRatePct);
    }

    [Fact]
    public void Code_validation_and_date_helpers()
    {
        Assert.Null(TaxRules.ValidateCode(" ivu-pr ", out var code));
        Assert.Equal("IVU-PR", code);
        Assert.Equal(TaxRules.CodeRequired, TaxRules.ValidateCode("  ", out _));
        Assert.Equal(TaxRules.CodeFormat, TaxRules.ValidateCode("IVU PR", out _));
        Assert.Equal(TaxRules.CodeTooLong, TaxRules.ValidateCode(new string('A', 31), out _));
        var d = new DateOnly(2026, 6, 30);
        Assert.True(TaxRules.InEffect(new DateOnly(2026, 1, 1), d, d));          // el último día rige
        Assert.False(TaxRules.InEffect(new DateOnly(2026, 1, 1), d, d.AddDays(1)));
        Assert.True(TaxRules.Overlaps(new DateOnly(2026, 1, 1), d, d, null));    // comparten el 30 de junio
        Assert.False(TaxRules.Overlaps(new DateOnly(2026, 1, 1), d, d.AddDays(1), null));
    }

    [Fact]
    public void Update_sql_section_creates_the_tax_tables_and_grants_the_permission_to_the_admin_only()
    {
        var sql = File.ReadAllText(Path.Combine(FindRoot(), "Diseño", "logistica-db-update.sql"));
        var i = sql.IndexOf("2026-10-15 (j)", StringComparison.Ordinal);
        Assert.True(i > 0);
        var section = sql[i..];
        foreach (var t in new[] { "dbo.TaxRate ", "dbo.TaxRateComponent", "dbo.TenantTaxSetting", "dbo.TaxChargeRule", "dbo.ClientTaxExemption" })
            Assert.Contains(t, section);
        Assert.Contains("TaxCategoryLookupId", section);
        Assert.Contains("'tax.manage'", section);
        Assert.Contains("r.Name IN ('TenantAdmin')", section);
        Assert.Contains(PermissionCatalog.TaxManage, PermissionCatalog.RoleTemplates["TenantAdmin"]);
        foreach (var kv in PermissionCatalog.RoleTemplates.Where(k => k.Key != "TenantAdmin")) Assert.DoesNotContain(PermissionCatalog.TaxManage, kv.Value);
    }

    [Fact]
    public void Tax_api_has_no_way_to_edit_or_delete_a_rate()
    {
        var methods = typeof(TaxController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.DoesNotContain(methods, m => m.GetCustomAttributes<HttpDeleteAttribute>().Any());
        var rateRoutes = methods.SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(a => (m.Name, a.HttpMethods, a.Template)))
            .Where(x => x.Template is not null && x.Template.StartsWith("rates")).ToList();
        Assert.DoesNotContain(rateRoutes, x => x.HttpMethods.Contains("PUT") || x.HttpMethods.Contains("PATCH"));
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Teikem.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró Teikem.sln");
    }
}

/// <summary>Catálogos de prueba leídos de la misma base (la base de pruebas no tiene el seed).</summary>
internal sealed class DbLookups(MessagingSqlFixture fx) : ILookupCache
{
    public async Task<int> GetIdAsync(string entity, string code, CancellationToken ct = default) => (await TryGetIdAsync(entity, code, ct)) ?? throw new InvalidOperationException($"{entity}/{code}");
    public async Task<int?> TryGetIdAsync(string entity, string code, CancellationToken ct = default)
    {
        await using var db = fx.NewDb(new TenantContext());
        return await db.Set<LookupCode>().Where(l => l.Entity == entity && l.InternalCode == code).Select(l => (int?)l.LookupCodeId).FirstOrDefaultAsync(ct);
    }
    public async Task<LookupCode?> GetAsync(int lookupCodeId, CancellationToken ct = default)
    {
        await using var db = fx.NewDb(new TenantContext());
        return await db.Set<LookupCode>().FirstOrDefaultAsync(l => l.LookupCodeId == lookupCodeId, ct);
    }
    public async Task<IReadOnlyList<LookupCode>> GetDomainAsync(string entity, CancellationToken ct = default)
    {
        await using var db = fx.NewDb(new TenantContext());
        return await db.Set<LookupCode>().Where(l => l.Entity == entity).ToListAsync(ct);
    }
    public void Invalidate() { }
}

// Contra SQL Server real: tasas con vigencia, reglas, exenciones y el cálculo completo de punta a punta.
public sealed class TaxSqlTests(MessagingSqlFixture fx) : IClassFixture<MessagingSqlFixture>, IAsyncLifetime
{
    private int A;
    private int B;
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly TenantClock Clock = new(LocalDay.DefaultZone, () => new DateTime(2026, 10, 15, 16, 0, 0, DateTimeKind.Utc));

    public async Task InitializeAsync()
    {
        if (fx.ConnectionString is null) return;
        (A, B) = (await fx.NewTenantAsync("Compañía A"), await fx.NewTenantAsync("Compañía B"));
        await using var db = fx.NewDb(new TenantContext());
        if (!await db.Set<LookupCode>().AnyAsync(l => l.Entity == "TaxCategory"))
        {
            foreach (var (e, c) in new[] { ("TaxCategory", "TAXABLE"), ("TaxCategory", "EXEMPT"), ("TaxCategory", "ZERO_RATE"),
                         ("ChargeType", "FREIGHT"), ("ChargeType", "HANDLING"), ("ChargeType", "PRODUCT_SALE") })
                db.Set<LookupCode>().Add(new LookupCode { Entity = e, InternalCode = c, LabelJson = $"{{\"es\":\"{c}\",\"en\":\"{c}\"}}", IsActive = true });
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private TaxService Svc(int tenantId) => new(fx.NewDb(new TenantContext { TenantId = tenantId, UserId = 1 }), new TenantContext { TenantId = tenantId, UserId = 1 }, Clock, new DbLookups(fx));

    private static TaxRateRequest Rate(string code, DateOnly from, DateOnly? to = null, decimal state = 10.5m, decimal city = 1m)
        => new(code, "Impuesto de ventas", from, to, new[] { new TaxComponentDto("STATE", "Estatal", state), new TaxComponentDto("CITY", "Municipal", city) });

    private async Task<int> ProductAsync(int tenantId, string category)
    {
        await using var db = fx.NewDb(new TenantContext { TenantId = tenantId });
        int? cat = category == "TAXABLE" ? null : await new DbLookups(fx).TryGetIdAsync("TaxCategory", category);
        var p = new Product { TenantId = tenantId, Sku = "SKU" + Guid.NewGuid().ToString("N")[..8], Name = "Producto", BaseUomLookupId = 1, TrackingTypeLookupId = 1, TaxCategoryLookupId = cat };
        db.Set<Product>().Add(p);
        await db.SaveChangesAsync();
        return p.ProductId;
    }

    private async Task<int> ClientAsync(int tenantId)
    {
        await using var db = fx.NewDb(new TenantContext { TenantId = tenantId });
        var c = new Client { TenantId = tenantId, Code = "C" + Guid.NewGuid().ToString("N")[..8], Name = "Cliente Uno", StatusCodeId = 1 };
        db.Set<Client>().Add(c);
        await db.SaveChangesAsync();
        return c.ClientId;
    }

    private static async Task<T> Thrown<T>(Func<Task> act) where T : Exception => await Assert.ThrowsAsync<T>(act);

    [SqlServerFact]
    public async Task A_rate_has_components_that_add_up_and_the_same_code_cannot_overlap_dates()
    {
        var svc = Svc(A);
        var rate = await svc.CreateRateAsync(Rate("ivu", new DateOnly(2026, 1, 1)), default);
        Assert.Equal("IVU", rate.Code);
        Assert.Equal(11.5m, rate.Percent);
        Assert.Equal("ACTIVE", rate.Status);
        Assert.Equal(2, rate.Components.Count);

        var overlap = await Thrown<ConflictException>(() => Svc(A).CreateRateAsync(Rate("IVU", new DateOnly(2026, 6, 1)), default));
        Assert.Equal(TaxRules.RateOverlap, overlap.Message);
        // otra compañía puede usar el mismo código
        Assert.Equal("IVU", (await Svc(B).CreateRateAsync(Rate("IVU", new DateOnly(2026, 1, 1)), default)).Code);
    }

    [SqlServerFact]
    public async Task Rate_validations_return_the_exact_messages()
    {
        string Msg(Exception e) => e.Message;
        Assert.Equal(TaxRules.CodeRequired, Msg(await Thrown<ValidationException>(() => Svc(A).CreateRateAsync(Rate("", Today), default))));
        Assert.Equal(TaxRules.ComponentsRequired, Msg(await Thrown<ValidationException>(() => Svc(A).CreateRateAsync(new TaxRateRequest("X1", "Algo", Today, null, Array.Empty<TaxComponentDto>()), default))));
        Assert.Equal(TaxRules.DatesOrder, Msg(await Thrown<ValidationException>(() => Svc(A).CreateRateAsync(Rate("X2", Today, Today.AddDays(-1)), default))));
        Assert.Equal(TaxRules.RatePercentRange, Msg(await Thrown<ValidationException>(() => Svc(A).CreateRateAsync(Rate("X3", Today, null, 90m, 20m), default))));
        Assert.Equal(TaxRules.ComponentDuplicate, Msg(await Thrown<ValidationException>(() => Svc(A).CreateRateAsync(
            new TaxRateRequest("X4", "Algo", Today, null, new[] { new TaxComponentDto("A", "Uno", 1), new TaxComponentDto("a", "Dos", 2) }), default))));
    }

    [SqlServerFact]
    public async Task Closing_a_rate_and_creating_the_new_one_keeps_history_and_the_date_picks_the_right_rate_even_mid_month()
    {
        var svc = Svc(A);
        var old = await svc.CreateRateAsync(Rate("IVU", new DateOnly(2026, 1, 1)), default);
        await Svc(A).UpdateSettingsAsync(new TaxSettingsRequest(DefaultRateCode: "IVU"), default);
        // la tasa cambia a mitad de mes: la vieja rige hasta el 14 y la nueva desde el 15 de octubre
        var closed = await Svc(A).CloseRateAsync(old.Id, new TaxRateCloseRequest(new DateOnly(2026, 10, 14)), default);
        Assert.Equal(new DateOnly(2026, 10, 14), closed.EffectiveTo);
        await Svc(A).CreateRateAsync(Rate("IVU", new DateOnly(2026, 10, 15), null, 11.5m, 1m), default);   // 12.5 %

        var line = new[] { new TaxCalcLineRequest("a", null, "PRODUCT_SALE", 100m) };
        var before = await Svc(A).CalculateAsync(new TaxCalcRequest(null, new DateOnly(2026, 10, 14), line), default);
        var after = await Svc(A).CalculateAsync(new TaxCalcRequest(null, new DateOnly(2026, 10, 15), line), default);
        Assert.Equal(11.5m, before.TaxAmount);
        Assert.Equal(12.5m, after.TaxAmount);
        Assert.NotEqual(before.Lines[0].TaxRateId, after.Lines[0].TaxRateId);   // cada línea guarda la tasa con que se calculó
        Assert.Equal(2, (await Svc(A).ListRatesAsync(false, default)).Count);
        Assert.Single(await Svc(A).ListRatesAsync(true, default));

        Assert.Equal(TaxRules.RateAlreadyClosed, (await Thrown<ConflictException>(() => Svc(A).CloseRateAsync(old.Id, new TaxRateCloseRequest(new DateOnly(2026, 11, 1)), default))).Message);
        Assert.Equal(TaxRules.NoRateInEffect, (await Thrown<ValidationException>(() => Svc(A).CalculateAsync(new TaxCalcRequest(null, new DateOnly(2025, 1, 1), line), default))).Message);
    }

    [SqlServerFact]
    public async Task Settings_default_then_change_rounding_included_prices_default_rate_and_charge_rules()
    {
        var s = await Svc(A).GetSettingsAsync(default);
        Assert.False(s.PricesIncludeTax);
        Assert.Equal("LINE", s.RoundingMode);
        Assert.Null(s.DefaultRateCode);
        Assert.DoesNotContain(s.ChargeRules, r => r.ChargeType == "PRODUCT_SALE");   // la venta de producto la decide el producto
        Assert.All(s.ChargeRules, r => Assert.False(r.IsTaxable));

        Assert.Equal(TaxRules.DefaultRateUnknown, (await Thrown<ValidationException>(() => Svc(A).UpdateSettingsAsync(new TaxSettingsRequest(DefaultRateCode: "NOPE"), default))).Message);
        Assert.Equal(TaxRules.RoundingInvalid, (await Thrown<ValidationException>(() => Svc(A).UpdateSettingsAsync(new TaxSettingsRequest(RoundingMode: "X"), default))).Message);
        Assert.Equal(TaxRules.ChargeTypeInvalid, (await Thrown<ValidationException>(() => Svc(A).UpdateSettingsAsync(new TaxSettingsRequest(ChargeRules: new[] { new TaxChargeRuleRequest("PRODUCT_SALE", true) }), default))).Message);

        var changed = await Svc(A).UpdateSettingsAsync(new TaxSettingsRequest(true, "document", null, null, new[] { new TaxChargeRuleRequest("FREIGHT", true) }), default);
        Assert.True(changed.PricesIncludeTax);
        Assert.Equal("DOCUMENT", changed.RoundingMode);
        Assert.True(changed.ChargeRules.Single(r => r.ChargeType == "FREIGHT").IsTaxable);
        Assert.False(changed.ChargeRules.Single(r => r.ChargeType == "HANDLING").IsTaxable);
        // las reglas de una compañía no se ven en la otra
        Assert.False((await Svc(B).GetSettingsAsync(default)).PricesIncludeTax);
    }

    [SqlServerFact]
    public async Task Calculation_uses_product_category_charge_rules_and_a_valid_client_exemption()
    {
        await Svc(A).CreateRateAsync(Rate("IVU", new DateOnly(2026, 1, 1)), default);
        await Svc(A).UpdateSettingsAsync(new TaxSettingsRequest(DefaultRateCode: "IVU", ChargeRules: new[] { new TaxChargeRuleRequest("FREIGHT", true) }), default);
        var taxable = await ProductAsync(A, "TAXABLE");
        var exempt = await ProductAsync(A, "EXEMPT");
        var zero = await ProductAsync(A, "ZERO_RATE");
        var lines = new[]
        {
            new TaxCalcLineRequest("gravable", taxable, null, 100m),
            new TaxCalcLineRequest("exento", exempt, null, 100m),
            new TaxCalcLineRequest("cero", zero, null, 100m),
            new TaxCalcLineRequest("flete", null, "FREIGHT", 50m),
            new TaxCalcLineRequest("manejo", null, "HANDLING", 20m),
        };
        var r = await Svc(A).CalculateAsync(new TaxCalcRequest(null, null, lines), default);
        Assert.Equal(new[] { "TAXABLE", "EXEMPT_PRODUCT", "ZERO_RATE", "TAXABLE", "NOT_TAXABLE_CHARGE" }, r.Lines.Select(l => l.Reason).ToArray());
        Assert.Equal(11.5m + 5.75m, r.TaxAmount);   // gravable + flete (la tasa por omisión: 11.5 %)
        Assert.Equal(370m, r.Subtotal);
        Assert.Equal(r.TaxAmount, r.Components.Sum(c => c.Amount));

        // cliente con exención vigente: nada paga impuesto, con el certificado anotado
        var client = await ClientAsync(A);
        var ex = await Svc(A).CreateExemptionAsync(new TaxExemptionRequest(client, "Sin fines de lucro", "CERT-001", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), default);
        Assert.Equal("VALID", ex.Status);
        var exemptSale = await Svc(A).CalculateAsync(new TaxCalcRequest(client, null, lines), default);
        Assert.Equal(0m, exemptSale.TaxAmount);
        Assert.Contains(exemptSale.Lines, l => l.Reason == "EXEMPT_CLIENT" && l.ExemptionCertificate == "CERT-001" && l.IsTaxExempt);
        Assert.Empty(exemptSale.Warnings);
    }

    [SqlServerFact]
    public async Task An_expired_client_exemption_warns_and_charges_the_tax()
    {
        await Svc(A).CreateRateAsync(Rate("IVU", new DateOnly(2026, 1, 1)), default);
        await Svc(A).UpdateSettingsAsync(new TaxSettingsRequest(DefaultRateCode: "IVU"), default);
        var client = await ClientAsync(A);
        var ex = await Svc(A).CreateExemptionAsync(new TaxExemptionRequest(client, "Gobierno", "CERT-OLD", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)), default);
        Assert.Equal("EXPIRED", ex.Status);
        var r = await Svc(A).CalculateAsync(new TaxCalcRequest(client, null, new[] { new TaxCalcLineRequest("a", null, "PRODUCT_SALE", 100m) }), default);
        Assert.Equal(11.5m, r.TaxAmount);
        Assert.Contains("CERT-OLD", Assert.Single(r.Warnings));

        // dada de baja una exención vigente tampoco exime
        var live = await Svc(A).CreateExemptionAsync(new TaxExemptionRequest(client, "Gobierno", "CERT-NEW", new DateOnly(2026, 1, 1), null), default);
        Assert.Equal(0m, (await Svc(A).CalculateAsync(new TaxCalcRequest(client, null, new[] { new TaxCalcLineRequest("a", null, "PRODUCT_SALE", 100m) }), default)).TaxAmount);
        Assert.Equal("INACTIVE", (await Svc(A).SetExemptionActiveAsync(live.Id, false, default)).Status);
        Assert.Equal(11.5m, (await Svc(A).CalculateAsync(new TaxCalcRequest(client, null, new[] { new TaxCalcLineRequest("a", null, "PRODUCT_SALE", 100m) }), default)).TaxAmount);
    }

    [SqlServerFact]
    public async Task Exemption_and_calculation_validations_return_the_exact_messages_and_other_companies_data_is_invisible()
    {
        var client = await ClientAsync(A);
        var otherClient = await ClientAsync(B);
        Assert.Equal("Cliente no encontrado.", (await Thrown<NotFoundException>(() => Svc(A).CreateExemptionAsync(new TaxExemptionRequest(otherClient, "X", "Y", Today), default))).Message);
        Assert.Equal(TaxRules.ExemptionTypeRequired, (await Thrown<ValidationException>(() => Svc(A).CreateExemptionAsync(new TaxExemptionRequest(client, " ", "Y", Today), default))).Message);
        Assert.Equal(TaxRules.CertificateRequired, (await Thrown<ValidationException>(() => Svc(A).CreateExemptionAsync(new TaxExemptionRequest(client, "X", "", Today), default))).Message);
        Assert.Equal(TaxRules.DatesOrder, (await Thrown<ValidationException>(() => Svc(A).CreateExemptionAsync(new TaxExemptionRequest(client, "X", "Y", Today, Today.AddDays(-1)), default))).Message);
        Assert.Equal(TaxRules.LinesRequired, (await Thrown<ValidationException>(() => Svc(A).CalculateAsync(new TaxCalcRequest(null, null, Array.Empty<TaxCalcLineRequest>()), default))).Message);
        Assert.Equal(TaxRules.NoDefaultRate, (await Thrown<ValidationException>(() => Svc(B).CalculateAsync(new TaxCalcRequest(null, null, new[] { new TaxCalcLineRequest("a", null, "PRODUCT_SALE", 10m) }), default))).Message);
        Assert.Equal("Producto no encontrado.", (await Thrown<NotFoundException>(() => Svc(A).CalculateAsync(new TaxCalcRequest(null, null, new[] { new TaxCalcLineRequest("a", 999999, null, 10m) }), default))).Message);

        await Svc(A).CreateExemptionAsync(new TaxExemptionRequest(client, "X", "Y", Today), default);
        Assert.Single(await Svc(A).ListExemptionsAsync(null, default));
        Assert.Empty(await Svc(B).ListExemptionsAsync(null, default));
    }

    [SqlServerFact]
    public async Task Product_tax_category_is_saved_as_null_when_taxable_and_validated()
    {
        var p = await ProductAsync(A, "TAXABLE");
        Assert.Equal("EXEMPT", (await Svc(A).SetProductCategoryAsync(p, new ProductTaxCategoryRequest("exempt"), default)).Category);
        Assert.Equal("EXEMPT", (await Svc(A).GetProductCategoryAsync(p, default)).Category);
        Assert.Equal("TAXABLE", (await Svc(A).SetProductCategoryAsync(p, new ProductTaxCategoryRequest("TAXABLE"), default)).Category);
        await using var db = fx.NewDb(new TenantContext { TenantId = A });
        Assert.Null((await db.Set<Product>().AsNoTracking().FirstAsync(x => x.ProductId == p)).TaxCategoryLookupId);
        // listado para la pantalla: filtros por categoría y buscador, solo de la compañía
        var other = await ProductAsync(A, "EXEMPT");
        var exempts = await Svc(A).ListProductCategoriesAsync(null, "exempt", 0, 50, default);
        Assert.Equal(new[] { other }, exempts.Items.Select(x => x.ProductId).ToArray());
        Assert.All(exempts.Items, x => Assert.Equal("EXEMPT", x.Category));
        var taxables = await Svc(A).ListProductCategoriesAsync(null, "TAXABLE", 0, 50, default);
        Assert.Contains(taxables.Items, x => x.ProductId == p && x.Category == "TAXABLE");
        Assert.DoesNotContain(taxables.Items, x => x.ProductId == other);
        Assert.Equal(2, (await Svc(A).ListProductCategoriesAsync(null, null, 0, 1, default)).Total);
        Assert.Empty((await Svc(B).ListProductCategoriesAsync(null, null, 0, 50, default)).Items);
        Assert.Equal(TaxRules.CategoryInvalid, (await Thrown<ValidationException>(() => Svc(A).ListProductCategoriesAsync(null, "X", 0, 50, default))).Message);
        Assert.Equal(TaxRules.CategoryInvalid, (await Thrown<ValidationException>(() => Svc(A).SetProductCategoryAsync(p, new ProductTaxCategoryRequest("OTRA"), default))).Message);
        Assert.Equal("Producto no encontrado.", (await Thrown<NotFoundException>(() => Svc(B).SetProductCategoryAsync(p, new ProductTaxCategoryRequest("EXEMPT"), default))).Message);
    }
}
