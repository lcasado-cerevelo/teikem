using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Tax;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Tax;
using Xunit;

namespace Teikem.Tests;

// Lote 237: exención del cliente POR COMPONENTES (p. ej. la municipal del IVU). Cálculo puro: parcial, total gana, vencida con aviso, componente que la
// tasa no tiene, combinación, redondeo por línea y por documento, precios con impuesto incluido; y la línea de IVU del IIF de QuickBooks.
public class TaxPartialExemptionTests
{
    private static readonly DateOnly Day = new(2026, 11, 10);

    private static TaxRateInfo Ivu(decimal state = 10.5m, decimal city = 1m)
        => new(1, "IVU", "IVU", state + city, new[] { new TaxComponentInfo("STATE", "Estatal", state), new TaxComponentInfo("CITY", "Municipal", city) });

    private static TaxLineInput L(string key, decimal amount, TaxTreatment t = TaxTreatment.Taxable) => new(key, amount, t);

    private static TaxExemptionInfo Partial(string cert, DateOnly from, DateOnly? to, params string[] codes) => new(TaxExemptionScopes.Components, codes, cert, from, to);
    private static TaxExemptionInfo Full(string cert, DateOnly from, DateOnly? to) => new(TaxExemptionScopes.All, Array.Empty<string>(), cert, from, to);

    [Fact]
    public void A_municipal_exemption_charges_only_the_state_component()
    {
        var r = TaxRules.Calculate(new[] { L("a", 100m) }, Ivu(), false, TaxRoundingModes.Line, new[] { "CITY" });
        var line = r.Lines[0];
        Assert.Equal(10.5m, line.TaxAmount);
        Assert.Equal(10.5m, line.TaxRatePct);                    // la copia de la línea: lo realmente cobrado, no 11.5
        Assert.Equal(1, line.TaxRateId);
        Assert.Equal(TaxReasons.ExemptClientPartial, line.Reason);
        Assert.False(line.IsTaxExempt);                          // paga impuesto: no es una línea exenta
        Assert.Equal(110.5m, line.Total);
        Assert.Equal(new[] { ("STATE", 10.5m, false), ("CITY", 0m, true) }, line.Components.Select(c => (c.Code, c.Amount, c.IsExempt)).ToArray());
        Assert.Equal(1m, line.Components[1].Percent);            // el porcentaje del componente exento se sigue viendo
        Assert.Equal(10.5m, r.TaxAmount);
        Assert.Equal(new[] { ("STATE", 10.5m, false), ("CITY", 0m, true) }, r.Components.Select(c => (c.Code, c.Amount, c.IsExempt)).ToArray());
    }

    [Fact]
    public void The_exempt_component_can_be_the_first_one_and_the_rest_absorbs_the_rounding()
    {
        // exento el estatal: se cobra solo el 1 % municipal
        var r = TaxRules.Calculate(new[] { L("a", 33.33m) }, Ivu(), false, TaxRoundingModes.Line, new[] { "state" });   // sin distinguir mayúsculas
        Assert.Equal(0.33m, r.Lines[0].TaxAmount);
        Assert.Equal(1m, r.Lines[0].TaxRatePct);
        Assert.Equal(new[] { 0m, 0.33m }, r.Lines[0].Components.Select(c => c.Amount).ToArray());
        // tres componentes, exento el del medio: los otros dos suman exactamente el impuesto
        var three = new TaxRateInfo(2, "IVU", "IVU", 11.5m, new[] { new TaxComponentInfo("STATE", "Estatal", 6m), new TaxComponentInfo("CITY", "Municipal", 1m), new TaxComponentInfo("SPECIAL", "Especial", 4.5m) });
        var t = TaxRules.Calculate(new[] { L("a", 0.77m) }, three, false, TaxRoundingModes.Line, new[] { "CITY" });
        Assert.Equal(0.08m, t.Lines[0].TaxAmount);               // 0.77 × 10.5 % = 0.08085
        Assert.Equal(0m, t.Lines[0].Components[1].Amount);
        Assert.Equal(t.Lines[0].TaxAmount, t.Lines[0].Components.Sum(c => c.Amount));
    }

    [Fact]
    public void A_component_the_rate_does_not_have_changes_nothing()
    {
        var r = TaxRules.Calculate(new[] { L("a", 100m) }, Ivu(), false, TaxRoundingModes.Line, new[] { "COUNTY" });
        Assert.Equal(11.5m, r.Lines[0].TaxAmount);
        Assert.Equal(11.5m, r.Lines[0].TaxRatePct);
        Assert.Equal(TaxReasons.Taxable, r.Lines[0].Reason);
        Assert.DoesNotContain(r.Lines[0].Components, c => c.IsExempt);
        Assert.Equal(new[] { "COUNTY" }, TaxRules.UnknownComponents(Ivu(), new[] { "COUNTY", "city" }));
        Assert.Empty(TaxRules.ExemptCodesOf(Ivu(), new[] { "COUNTY" }));
        // un componente que la tasa tiene en 0 % tampoco cambia nada
        var zeroCity = TaxRules.Calculate(new[] { L("a", 100m) }, Ivu(11.5m, 0m), false, TaxRoundingModes.Line, new[] { "CITY" });
        Assert.Equal((11.5m, TaxReasons.Taxable), (zeroCity.Lines[0].TaxAmount, zeroCity.Lines[0].Reason));
    }

    [Fact]
    public void Exempting_every_component_is_a_full_exemption()
    {
        var r = TaxRules.Calculate(new[] { L("a", 100m) }, Ivu(), false, TaxRoundingModes.Line, new[] { "CITY", "STATE" });
        Assert.Equal(0m, r.TaxAmount);
        Assert.Equal(TaxReasons.ExemptClient, r.Lines[0].Reason);
        Assert.True(r.Lines[0].IsTaxExempt);
        Assert.Equal(0m, r.Lines[0].TaxRatePct);
        Assert.Null(r.Lines[0].TaxRateId);
    }

    [Fact]
    public void Only_taxable_lines_change_exempt_products_zero_rate_and_non_taxable_charges_stay_the_same()
    {
        var r = TaxRules.Calculate(new[] { L("g", 100m), L("e", 100m, TaxTreatment.ExemptProduct), L("z", 100m, TaxTreatment.ZeroRate), L("c", 100m, TaxTreatment.NotTaxableCharge) },
            Ivu(), false, TaxRoundingModes.Line, new[] { "CITY" });
        Assert.Equal(new[] { TaxReasons.ExemptClientPartial, TaxReasons.ExemptProduct, TaxReasons.ZeroRate, TaxReasons.NotTaxableCharge }, r.Lines.Select(l => l.Reason).ToArray());
        Assert.Equal(new[] { 10.5m, 0m, 0m, 0m }, r.Lines.Select(l => l.TaxAmount).ToArray());
        Assert.Equal(10.5m, r.TaxAmount);
        Assert.Equal(410.5m, r.Total);
    }

    [Fact]
    public void Rounding_by_line_and_by_document_with_a_partial_exemption()
    {
        // 0.33 × 10.5 % = 0.03465 por línea
        var lines = new[] { L("a", 0.33m), L("b", 0.33m), L("c", 0.33m) };
        var byLine = TaxRules.Calculate(lines, Ivu(), false, TaxRoundingModes.Line, new[] { "CITY" });
        var byDoc = TaxRules.Calculate(lines, Ivu(), false, TaxRoundingModes.Document, new[] { "CITY" });
        Assert.Equal(new[] { 0.03m, 0.03m, 0.03m }, byLine.Lines.Select(l => l.TaxAmount).ToArray());
        Assert.Equal(0.09m, byLine.TaxAmount);
        Assert.Equal(new[] { 0.0347m, 0.0347m, 0.0347m }, byDoc.Lines.Select(l => l.TaxAmount).ToArray());   // 4 decimales por línea
        Assert.Equal(0.10m, byDoc.TaxAmount);                    // 0.10395 redondeado una sola vez
        Assert.Equal(0.99m + 0.10m, byDoc.Total);
        foreach (var doc in new[] { byLine, byDoc })
        {
            Assert.Equal(doc.TaxAmount, doc.Components.Sum(c => c.Amount));
            Assert.Equal(0m, doc.Components.Single(c => c.Code == "CITY").Amount);
            Assert.True(doc.Components.Single(c => c.Code == "CITY").IsExempt);
            Assert.All(doc.Lines, l => Assert.Equal(10.5m, l.TaxRatePct));
        }
    }

    [Fact]
    public void Prices_with_tax_included_extract_only_the_charged_part()
    {
        var r = TaxRules.Calculate(new[] { L("a", 110.5m) }, Ivu(), true, TaxRoundingModes.Line, new[] { "CITY" });
        Assert.Equal(10.5m, r.Lines[0].TaxAmount);
        Assert.Equal(100m, r.Lines[0].Net);
        Assert.Equal(110.5m, r.Total);
    }

    [Fact]
    public void Without_exempt_components_the_calculation_is_the_same_as_before()
    {
        var before = TaxRules.Calculate(new[] { L("a", 10m), L("b", 20m) }, Ivu(), false, TaxRoundingModes.Line);
        var withEmpty = TaxRules.Calculate(new[] { L("a", 10m), L("b", 20m) }, Ivu(), false, TaxRoundingModes.Line, Array.Empty<string>());
        Assert.Equal(3.45m, before.TaxAmount);
        Assert.Equal(before.TaxAmount, withEmpty.TaxAmount);
        Assert.All(before.Components, c => Assert.False(c.IsExempt));
    }

    // ---------------------------------------------------------------- qué exenciones aplican en una fecha

    [Fact]
    public void A_partial_exemption_in_effect_gives_its_components_with_the_certificate()
    {
        var o = TaxRules.ResolveExemptions(new[] { Partial("MUN-1", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "CITY") }, Day);
        Assert.Null(o.Full);
        Assert.Equal("MUN-1", o.ComponentCertificates["CITY"]);
        Assert.Equal("MUN-1", o.ComponentCertificates["city"]);   // sin distinguir mayúsculas
        Assert.Null(o.Expired);
    }

    [Fact]
    public void A_full_exemption_in_effect_wins_over_the_partial_ones()
    {
        var o = TaxRules.ResolveExemptions(new[]
        {
            Partial("MUN-1", new DateOnly(2026, 1, 1), null, "CITY"),
            Full("TOTAL-9", new DateOnly(2026, 6, 1), null),
            Full("VIEJA", new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)),
        }, Day);
        Assert.Equal("TOTAL-9", o.Full?.CertificateNumber);
        Assert.Empty(o.ComponentCertificates);
        Assert.Null(o.Expired);   // con una total vigente no hay nada que avisar
    }

    [Fact]
    public void Several_partial_exemptions_in_effect_are_combined()
    {
        var o = TaxRules.ResolveExemptions(new[]
        {
            Partial("MUN-1", new DateOnly(2026, 1, 1), null, "CITY"),
            Partial("EST-2", new DateOnly(2026, 3, 1), null, "STATE", "CITY"),
            Partial("FUTURA", new DateOnly(2027, 1, 1), null, "SPECIAL"),
        }, Day);
        Assert.Null(o.Full);
        Assert.Equal(new[] { "CITY", "STATE" }, o.ComponentCertificates.Keys.OrderBy(k => k).ToArray());   // la que empieza en 2027 todavía no cuenta
        Assert.Equal("EST-2", o.ComponentCertificates["CITY"]);   // el certificado de la más reciente que lo cubre
        // juntas cubren toda la tasa: exención completa
        var r = TaxRules.Calculate(new[] { L("a", 100m) }, Ivu(), false, TaxRoundingModes.Line, o.ComponentCertificates.Keys.ToList());
        Assert.Equal((0m, TaxReasons.ExemptClient), (r.TaxAmount, r.Lines[0].Reason));
    }

    [Fact]
    public void An_expired_exemption_does_not_apply_and_is_reported()
    {
        var o = TaxRules.ResolveExemptions(new[] { Partial("MUN-VENCIDO", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), "CITY") }, Day);
        Assert.Null(o.Full);
        Assert.Empty(o.ComponentCertificates);
        Assert.Equal("MUN-VENCIDO", o.Expired?.CertificateNumber);
        // el último día de vigencia todavía aplica; al día siguiente ya no
        var edge = new[] { Partial("MUN-1", new DateOnly(2026, 1, 1), Day, "CITY") };
        Assert.Single(TaxRules.ResolveExemptions(edge, Day).ComponentCertificates);
        Assert.Empty(TaxRules.ResolveExemptions(edge, Day.AddDays(1)).ComponentCertificates);
        Assert.Equal("MUN-1", TaxRules.ResolveExemptions(edge, Day.AddDays(1)).Expired?.CertificateNumber);
    }

    [Fact]
    public void A_renewed_certificate_does_not_warn_but_an_expired_one_that_covered_more_does()
    {
        // municipal vencido y renovado: nada que avisar
        var renewed = TaxRules.ResolveExemptions(new[]
        {
            Partial("MUN-2025", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), "CITY"),
            Partial("MUN-2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "CITY"),
        }, Day);
        Assert.Null(renewed.Expired);
        // venció una TOTAL y solo queda la municipal: se avisa (el estatal se está cobrando)
        var less = TaxRules.ResolveExemptions(new[]
        {
            Full("TOTAL-2025", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),
            Partial("MUN-2026", new DateOnly(2026, 1, 1), null, "CITY"),
        }, Day);
        Assert.Equal("TOTAL-2025", less.Expired?.CertificateNumber);
        Assert.Single(less.ComponentCertificates);
    }

    // ---------------------------------------------------------------- validación del alcance (mensajes exactos del manual)

    [Fact]
    public void Scope_validation_normalizes_and_returns_the_exact_messages()
    {
        Assert.Null(TaxRules.ValidateExemptionScope(null, null, out var scope, out var codes));
        Assert.Equal((TaxExemptionScopes.All, 0), (scope, codes.Count));                                    // sin alcance = todo el impuesto
        Assert.Null(TaxRules.ValidateExemptionScope("all", new[] { "CITY" }, out scope, out codes));
        Assert.Equal((TaxExemptionScopes.All, 0), (scope, codes.Count));                                    // con ALL los componentes no se guardan
        Assert.Null(TaxRules.ValidateExemptionScope(" components ", new[] { " state", "city", "CITY", "" }, out scope, out codes));
        Assert.Equal(TaxExemptionScopes.Components, scope);
        Assert.Equal(new[] { "CITY", "STATE" }, codes);                                                      // mayúsculas, sin repetir, ordenados
        Assert.Equal("El alcance de la exención debe ser ALL (todo el impuesto) o COMPONENTS (solo algunos componentes).", TaxRules.ValidateExemptionScope("PARTE", null, out _, out _));
        Assert.Equal("Marque al menos un componente del impuesto que la exención cubre.", TaxRules.ValidateExemptionScope("COMPONENTS", null, out _, out _));
        Assert.Equal("Marque al menos un componente del impuesto que la exención cubre.", TaxRules.ValidateExemptionScope("COMPONENTS", new[] { " " }, out _, out _));
        Assert.Equal("El código de un componente no es válido (letras, números, guion y guion bajo; hasta 30 caracteres).", TaxRules.ValidateExemptionScope("COMPONENTS", new[] { "MUNI CIPAL" }, out _, out _));
        Assert.Equal("Una exención puede nombrar hasta 10 componentes.", TaxRules.ValidateExemptionScope("COMPONENTS", Enumerable.Range(1, 11).Select(i => "C" + i).ToArray(), out _, out _));
        Assert.Equal(new[] { "CITY", "STATE" }, TaxRules.SplitComponentCodes("CITY,STATE"));
        Assert.Empty(TaxRules.SplitComponentCodes(null));
    }

    // ---------------------------------------------------------------- QuickBooks IIF (Lote 225): la línea de IVU lleva lo realmente cobrado

    [Fact]
    public void The_IIF_tax_line_carries_the_partial_tax_and_the_line_is_still_taxable()
    {
        // factura de un cliente con exención municipal: 2 × 10.00 al 10.5 % = 2.10 (no 2.30)
        var calc = TaxRules.Calculate(new[] { L("a", 20m) }, Ivu(), false, TaxRoundingModes.Line, new[] { "CITY" });
        Assert.Equal(2.10m, calc.TaxAmount);
        var inv = new QuickBooksInvoice("FAC-00001", new DateOnly(2026, 11, 2), new DateOnly(2026, 12, 2), "Farmacia Central", "Net 30", "OV-00012",
            new[] { new QuickBooksInvoiceLine("MED-1", "Guantes", 2m, 10m, true) }, calc.TaxAmount);
        var rows = QuickBooksIifWriter.Write(QuickBooksAccounts.Defaults, Array.Empty<QuickBooksItem>(), Array.Empty<QuickBooksCustomer>(), new[] { inv })
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Select(r => r.Split('\t')).ToList();
        var trns = rows.Single(r => r[0] == "TRNS");
        var spl = rows.Where(r => r[0] == "SPL").ToList();
        Assert.Equal("22.10", trns[6]);                          // total = 20.00 + 2.10
        Assert.Equal(("-20.00", "Y"), (spl[0][6], spl[0][13]));  // la línea de producto sigue gravable
        Assert.Equal(("-2.10", "AUTOSTAX"), (spl[1][6], spl[1][14]));
    }

    [Fact]
    public void The_update_script_adds_the_scope_columns_idempotently_and_keeps_existing_exemptions_as_full()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Teikem.sln"))) dir = dir.Parent;
        var sql = File.ReadAllText(Path.Combine(dir!.FullName, "Diseño", "logistica-db-update.sql"));
        var i = sql.IndexOf("Lote 237: exención del cliente POR COMPONENTES", StringComparison.Ordinal);
        Assert.True(i > 0);
        var section = sql[i..];
        Assert.Contains("IF COL_LENGTH(N'dbo.ClientTaxExemption', N'Scope') IS NULL", section);
        Assert.Contains("DEFAULT N'ALL'", section);
        Assert.Contains("IF COL_LENGTH(N'dbo.ClientTaxExemption', N'ComponentCodes') IS NULL", section);
        Assert.Contains("IF OBJECT_ID(N'dbo.CK_ClientTaxExemption_Scope', N'C') IS NULL", section);
    }
}

// Lote 237 contra SQL Server real: la exención parcial de punta a punta por TaxService (lo mismo que usan la orden de venta, el envío con IVU, la factura y
// la nota de crédito: todos llaman a CalculateAsync con el cliente y copian TaxRatePct / TaxAmount de la línea).
public sealed class TaxPartialExemptionSqlTests(MessagingSqlFixture fx) : IClassFixture<MessagingSqlFixture>, IAsyncLifetime
{
    private int A;
    private static readonly TenantClock Clock = new(LocalDay.DefaultZone, () => new DateTime(2026, 11, 10, 16, 0, 0, DateTimeKind.Utc));

    public async Task InitializeAsync()
    {
        if (fx.ConnectionString is null) return;
        A = await fx.NewTenantAsync("Compañía L237");
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

    private TaxService Svc() => new(fx.NewDb(new TenantContext { TenantId = A, UserId = 1 }), new TenantContext { TenantId = A, UserId = 1 }, Clock, new DbLookups(fx));

    private async Task SetUpRateAsync()
    {
        await Svc().CreateRateAsync(new TaxRateRequest("IVU", "Impuesto de ventas", new DateOnly(2026, 1, 1), null,
            new[] { new TaxComponentDto("STATE", "Estatal", 10.5m), new TaxComponentDto("CITY", "Municipal", 1m) }), default);
        await Svc().UpdateSettingsAsync(new TaxSettingsRequest(DefaultRateCode: "IVU"), default);
    }

    private async Task<int> ProductAsync(string category)
    {
        await using var db = fx.NewDb(new TenantContext { TenantId = A });
        int? cat = category == "TAXABLE" ? null : await new DbLookups(fx).TryGetIdAsync("TaxCategory", category);
        var p = new Product { TenantId = A, Sku = "SKU" + Guid.NewGuid().ToString("N")[..8], Name = "Producto", BaseUomLookupId = 1, TrackingTypeLookupId = 1, TaxCategoryLookupId = cat };
        db.Set<Product>().Add(p);
        await db.SaveChangesAsync();
        return p.ProductId;
    }

    private async Task<int> ClientAsync()
    {
        await using var db = fx.NewDb(new TenantContext { TenantId = A });
        var c = new Client { TenantId = A, Code = "C" + Guid.NewGuid().ToString("N")[..8], Name = "Farmacia Central", StatusCodeId = 1 };
        db.Set<Client>().Add(c);
        await db.SaveChangesAsync();
        return c.ClientId;
    }

    [SqlServerFact]
    public async Task A_municipal_exemption_in_effect_charges_only_the_state_part_and_notes_the_certificate()
    {
        await SetUpRateAsync();
        var client = await ClientAsync();
        var taxable = await ProductAsync("TAXABLE");
        var exempt = await ProductAsync("EXEMPT");
        var ex = await Svc().CreateExemptionAsync(new TaxExemptionRequest(client, "Municipal", "MUN-001", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            Scope: "components", ComponentCodes: new[] { "city" }), default);
        Assert.Equal(("COMPONENTS", "VALID"), (ex.Scope, ex.Status));
        Assert.Equal(new[] { "CITY" }, ex.ComponentCodes);
        Assert.Equal("CITY", Assert.Single(await Svc().ListExemptionsAsync(client, default)).ComponentCodes!.Single());

        var r = await Svc().CalculateAsync(new TaxCalcRequest(client, null, new[]
        {
            new TaxCalcLineRequest("gravable", taxable, null, 100m),
            new TaxCalcLineRequest("exento", exempt, null, 100m),
            new TaxCalcLineRequest("envio", null, "PRODUCT_SALE", 10m),   // así calcula el IVU del envío la orden de venta (Lote 222)
        }), default);
        Assert.Equal(new[] { "EXEMPT_CLIENT_PARTIAL", "EXEMPT_PRODUCT", "EXEMPT_CLIENT_PARTIAL" }, r.Lines.Select(l => l.Reason).ToArray());
        Assert.Equal(new[] { 10.5m, 0m, 1.05m }, r.Lines.Select(l => l.TaxAmount).ToArray());
        Assert.Equal(new[] { 10.5m, 0m, 10.5m }, r.Lines.Select(l => l.TaxRatePct).ToArray());
        Assert.Equal(new[] { false, true, false }, r.Lines.Select(l => l.IsTaxExempt).ToArray());
        Assert.Equal("MUN-001", r.Lines[0].ExemptionCertificate);
        Assert.Null(r.Lines[1].ExemptionCertificate);
        Assert.Equal(11.5m, r.RatePercent);                      // la tasa completa; lo cobrado va en cada línea
        Assert.Equal(11.55m, r.TaxAmount);
        var city = r.Components.Single(c => c.Code == "CITY");
        Assert.Equal((0m, "EXEMPT_CLIENT_PARTIAL", "MUN-001"), (city.Amount, city.ExemptReason, city.ExemptionCertificate));
        var state = r.Components.Single(c => c.Code == "STATE");
        Assert.Equal((11.55m, (string?)null, (string?)null), (state.Amount, state.ExemptReason, state.ExemptionCertificate));
        Assert.Empty(r.Warnings);

        // en una fecha en que la exención ya venció: impuesto completo y el aviso de siempre con el certificado
        var later = await Svc().CalculateAsync(new TaxCalcRequest(client, new DateOnly(2027, 1, 15), new[] { new TaxCalcLineRequest("gravable", taxable, null, 100m) }), default);
        Assert.Equal((11.5m, 11.5m, "TAXABLE"), (later.TaxAmount, later.Lines[0].TaxRatePct, later.Lines[0].Reason));
        var warning = Assert.Single(later.Warnings);
        Assert.StartsWith("El cliente tiene una exención vencida (certificado MUN-001, venció el ", warning);
        Assert.EndsWith("): se cobró el impuesto.", warning);
        // sin cliente, la venta paga todo
        Assert.Equal(11.5m, (await Svc().CalculateAsync(new TaxCalcRequest(null, null, new[] { new TaxCalcLineRequest("gravable", taxable, null, 100m) }), default)).TaxAmount);
    }

    [SqlServerFact]
    public async Task Exemptions_are_combined_a_full_one_wins_and_a_component_missing_from_the_rate_only_warns()
    {
        await SetUpRateAsync();
        // una tasa vieja (cerrada) con un componente que la vigente ya no tiene
        await Svc().CreateRateAsync(new TaxRateRequest("OLD", "Tasa vieja", new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31), new[] { new TaxComponentDto("SPECIAL", "Especial", 2m) }), default);
        var client = await ClientAsync();
        var line = new[] { new TaxCalcLineRequest("a", null, "PRODUCT_SALE", 200m) };
        await Svc().CreateExemptionAsync(new TaxExemptionRequest(client, "Especial", "ESP-7", new DateOnly(2026, 1, 1), null, Scope: "COMPONENTS", ComponentCodes: new[] { "SPECIAL" }), default);
        var onlyUnknown = await Svc().CalculateAsync(new TaxCalcRequest(client, null, line), default);
        Assert.Equal((23m, "TAXABLE"), (onlyUnknown.TaxAmount, onlyUnknown.Lines[0].Reason));
        Assert.Equal("La exención del cliente (certificado ESP-7) nombra el componente SPECIAL, que la tasa vigente no tiene: no cambió el impuesto.", Assert.Single(onlyUnknown.Warnings));

        await Svc().CreateExemptionAsync(new TaxExemptionRequest(client, "Municipal", "MUN-1", new DateOnly(2026, 1, 1), null, Scope: "COMPONENTS", ComponentCodes: new[] { "CITY" }), default);
        var municipal = await Svc().CalculateAsync(new TaxCalcRequest(client, null, line), default);
        Assert.Equal((21m, 10.5m, "MUN-1"), (municipal.TaxAmount, municipal.Lines[0].TaxRatePct, municipal.Lines[0].ExemptionCertificate));

        var state = await Svc().CreateExemptionAsync(new TaxExemptionRequest(client, "Estatal", "EST-2", new DateOnly(2026, 2, 1), null, Scope: "COMPONENTS", ComponentCodes: new[] { "STATE" }), default);
        var both = await Svc().CalculateAsync(new TaxCalcRequest(client, null, line), default);
        Assert.Equal((0m, "EXEMPT_CLIENT", true), (both.TaxAmount, both.Lines[0].Reason, both.Lines[0].IsTaxExempt));   // las dos juntas cubren toda la tasa
        Assert.Equal("EST-2, MUN-1", both.Lines[0].ExemptionCertificate);
        await Svc().SetExemptionActiveAsync(state.Id, false, default);   // dada de baja: ya no cuenta

        var full = await Svc().CreateExemptionAsync(new TaxExemptionRequest(client, "Gobierno", "TOTAL-9", new DateOnly(2026, 1, 1), null), default);
        Assert.Equal("ALL", full.Scope);
        Assert.Empty(full.ComponentCodes!);
        var total = await Svc().CalculateAsync(new TaxCalcRequest(client, null, line), default);
        Assert.Equal((0m, "EXEMPT_CLIENT", "TOTAL-9"), (total.TaxAmount, total.Lines[0].Reason, total.Lines[0].ExemptionCertificate));
        Assert.Empty(total.Warnings);   // con la total vigente ni siquiera se resuelve la tasa
    }

    [SqlServerFact]
    public async Task Scope_is_validated_saved_normalized_and_patched()
    {
        await SetUpRateAsync();
        var client = await ClientAsync();
        var from = new DateOnly(2026, 1, 1);
        async Task<string> Bad(TaxExemptionRequest req) => (await Assert.ThrowsAsync<ValidationException>(() => Svc().CreateExemptionAsync(req, default))).Message;
        Assert.Equal(TaxRules.ExemptionScopeInvalid, await Bad(new TaxExemptionRequest(client, "X", "Y", from, Scope: "MITAD")));
        Assert.Equal(TaxRules.ExemptionComponentsRequired, await Bad(new TaxExemptionRequest(client, "X", "Y", from, Scope: "COMPONENTS")));
        Assert.Equal(TaxRules.ExemptionComponentsRequired, await Bad(new TaxExemptionRequest(client, "X", "Y", from, Scope: "COMPONENTS", ComponentCodes: Array.Empty<string>())));
        Assert.Equal(TaxRules.ExemptionComponentInvalid, await Bad(new TaxExemptionRequest(client, "X", "Y", from, Scope: "COMPONENTS", ComponentCodes: new[] { "CI TY" })));
        Assert.Equal("Ninguna tasa de la compañía tiene el componente «COUNTY».", await Bad(new TaxExemptionRequest(client, "X", "Y", from, Scope: "COMPONENTS", ComponentCodes: new[] { "CITY", "COUNTY" })));

        var e = await Svc().CreateExemptionAsync(new TaxExemptionRequest(client, "Municipal", "MUN-1", from, Scope: "COMPONENTS", ComponentCodes: new[] { "state", "CITY", "city" }), default);
        Assert.Equal(new[] { "CITY", "STATE" }, e.ComponentCodes);
        await using (var db = fx.NewDb(new TenantContext { TenantId = A }))
            Assert.Equal(("COMPONENTS", "CITY,STATE"), await db.Set<ClientTaxExemption>().Where(x => x.ClientTaxExemptionId == e.Id).Select(x => new ValueTuple<string, string?>(x.Scope, x.ComponentCodes)).SingleAsync());

        // PATCH: lo que no viene no cambia; solo los componentes; a «todo el impuesto» los quita; volver a parcial sin componentes no se puede
        var notes = await Svc().UpdateExemptionAsync(e.Id, new TaxExemptionPatchRequest(Notes: "Renovar en diciembre"), default);
        Assert.Equal(("COMPONENTS", 2), (notes.Scope, notes.ComponentCodes!.Count));
        var onlyCity = await Svc().UpdateExemptionAsync(e.Id, new TaxExemptionPatchRequest(ComponentCodes: new[] { "CITY" }), default);
        Assert.Equal(new[] { "CITY" }, onlyCity.ComponentCodes);
        var all = await Svc().UpdateExemptionAsync(e.Id, new TaxExemptionPatchRequest(Scope: "ALL"), default);
        Assert.Equal("ALL", all.Scope);
        Assert.Empty(all.ComponentCodes!);
        Assert.Equal(TaxRules.ExemptionComponentsRequired,
            (await Assert.ThrowsAsync<ValidationException>(() => Svc().UpdateExemptionAsync(e.Id, new TaxExemptionPatchRequest(Scope: "COMPONENTS"), default))).Message);
        var back = await Svc().UpdateExemptionAsync(e.Id, new TaxExemptionPatchRequest(Scope: "COMPONENTS", ComponentCodes: new[] { "CITY" }), default);
        Assert.Equal(("COMPONENTS", "CITY"), (back.Scope, back.ComponentCodes!.Single()));
    }

    [SqlServerFact]
    public async Task Rounding_by_document_and_prices_with_tax_included_respect_the_partial_exemption()
    {
        await SetUpRateAsync();
        var client = await ClientAsync();
        await Svc().CreateExemptionAsync(new TaxExemptionRequest(client, "Municipal", "MUN-1", new DateOnly(2026, 1, 1), null, Scope: "COMPONENTS", ComponentCodes: new[] { "CITY" }), default);
        var lines = Enumerable.Range(1, 3).Select(i => new TaxCalcLineRequest("l" + i, null, "PRODUCT_SALE", 0.33m)).ToArray();
        Assert.Equal(0.09m, (await Svc().CalculateAsync(new TaxCalcRequest(client, null, lines), default)).TaxAmount);          // por línea: 0.03 × 3
        await Svc().UpdateSettingsAsync(new TaxSettingsRequest(RoundingMode: "DOCUMENT"), default);
        var byDoc = await Svc().CalculateAsync(new TaxCalcRequest(client, null, lines), default);
        Assert.Equal(0.10m, byDoc.TaxAmount);                                                                                  // 0.10395 una sola vez
        Assert.Equal((0.10m, 0m), (byDoc.Components.Single(c => c.Code == "STATE").Amount, byDoc.Components.Single(c => c.Code == "CITY").Amount));
        await Svc().UpdateSettingsAsync(new TaxSettingsRequest(PricesIncludeTax: true, RoundingMode: "LINE"), default);
        var included = await Svc().CalculateAsync(new TaxCalcRequest(client, null, new[] { new TaxCalcLineRequest("a", null, "PRODUCT_SALE", 110.5m) }), default);
        Assert.Equal((100m, 10.5m, 110.5m), (included.Lines[0].Net, included.Lines[0].TaxAmount, included.Total));
    }
}
