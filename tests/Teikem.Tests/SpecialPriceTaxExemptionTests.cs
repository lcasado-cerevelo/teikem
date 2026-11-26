using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 247 (P2, pedido del dueño, DECISIÓN 20): el precio especial es un precio ANTES del IVU. Las exenciones del cliente (certificado estatal, municipal, los dos,
// o la exención total) se aplican DESPUÉS sobre el precio que ganó, sin cambiar el cálculo. Cliente de cadena con precio especial 8.00 (general 10); IVU
// estatal 10.5 % + municipal 1 %. En todos los casos la línea cobra 8.00.
public sealed class SpecialPriceTaxExemptionTests
{
    private sealed record Case(string Name, decimal ExpectedTax, Func<TaxService, int, Task> Exemptions, string? Reason = null, bool ExpectsExpiredWarning = false);

    [Fact]
    public async Task The_special_price_is_before_tax_and_every_exemption_applies_on_top_of_it()
    {
        await using var x = await PriceWorld.CreateAsync();
        x.F.SetModules(ModuleKeys.SalesOrders, ModuleKeys.Tax);
        var tax = x.F.Get<TaxService>();
        await tax.CreateRateAsync(new TaxRateRequest("IVU", "IVU", new DateOnly(2020, 1, 1), null,
            new[] { new TaxComponentDto("STATE", "Estatal", 10.5m), new TaxComponentDto("CITY", "Municipal", 1m) }), default);
        await tax.UpdateSettingsAsync(new TaxSettingsRequest(DefaultRateCode: "IVU"), default);
        var walgreens = await x.ChainAsync("Walgreens");
        var strips = await x.ProductAsync("52800", 12m);
        await x.PublishedListAsync("General", x.Today.AddDays(-30), null, null, (strips, 10m));
        await x.ChainListAsync("Precio especial — Walgreens", x.Today.AddDays(-30), null, walgreens, special: true, (strips, 8m));

        var from = x.Today.AddDays(-60);
        Task City(TaxService t, int c, string cert = "MUN-1") => t.CreateExemptionAsync(new TaxExemptionRequest(c, "Municipal", cert, from, null, Scope: "COMPONENTS", ComponentCodes: new[] { "CITY" }), default);
        Task State(TaxService t, int c) => t.CreateExemptionAsync(new TaxExemptionRequest(c, "Estatal", "EST-1", from, null, Scope: "COMPONENTS", ComponentCodes: new[] { "STATE" }), default);
        Task Full(TaxService t, int c, DateOnly? to = null) => t.CreateExemptionAsync(new TaxExemptionRequest(c, "Total", "TOTAL-1", from, to), default);
        var cases = new[]
        {
            new Case("sin exención", 0.92m, (_, _) => Task.CompletedTask, "TAXABLE"),
            new Case("certificado municipal", 0.84m, (t, c) => City(t, c), "EXEMPT_CLIENT_PARTIAL"),
            new Case("certificado estatal", 0.08m, (t, c) => State(t, c), "EXEMPT_CLIENT_PARTIAL"),
            new Case("los dos certificados", 0m, async (t, c) => { await City(t, c); await State(t, c); }, "EXEMPT_CLIENT"),
            new Case("exención total", 0m, (t, c) => Full(t, c), "EXEMPT_CLIENT"),
            new Case("total más parcial", 0m, async (t, c) => { await City(t, c); await Full(t, c); }, "EXEMPT_CLIENT"),
            new Case("total vencida más municipal vigente", 0.84m, async (t, c) => { await Full(t, c, x.Today.AddDays(-1)); await City(t, c, "MUN-2"); }, "EXEMPT_CLIENT_PARTIAL", true),
        };
        var n = 0;
        foreach (var k in cases)
        {
            var client = await x.ClientInAsync("CT" + ++n, walgreens);
            await k.Exemptions(tax, client.ClientId);
            var o = await x.DraftAsync(client, new SalesOrderLineRequest(strips.PublicId, 1m));
            var line = o.Lines[0];
            Assert.True(8m == line.UnitPrice, $"{k.Name}: precio {line.UnitPrice}");
            Assert.True(PriceSources.ChainList == line.PriceSource, $"{k.Name}: origen {line.PriceSource}");
            Assert.True(k.ExpectedTax == line.TaxAmount, $"{k.Name}: impuesto {line.TaxAmount}, se esperaba {k.ExpectedTax}");
            Assert.True(k.ExpectedTax == o.TaxAmount, $"{k.Name}: impuesto de la orden {o.TaxAmount}");
            Assert.True(8m + k.ExpectedTax == o.Total, $"{k.Name}: total {o.Total}");
            Assert.True((k.ExpectedTax == 0m) == line.IsTaxExempt, $"{k.Name}: exenta {line.IsTaxExempt}");

            // el mismo cálculo que hizo la orden, sobre el precio que ganó: la razón y el aviso de la exención vencida
            var calc = await tax.CalculateAsync(new TaxCalcRequest(client.ClientId, null, new[] { new TaxCalcLineRequest("a", strips.ProductId, null, line.UnitPrice) }), default);
            Assert.True(k.ExpectedTax == calc.TaxAmount, $"{k.Name}: cálculo {calc.TaxAmount}");
            Assert.True(k.Reason == calc.Lines[0].Reason, $"{k.Name}: razón {calc.Lines[0].Reason}");
            if (k.ExpectsExpiredWarning)
                Assert.Contains(calc.Warnings, w => w.StartsWith("El cliente tiene una exención vencida (certificado TOTAL-1", StringComparison.Ordinal));
            else
                Assert.Empty(calc.Warnings);
        }
    }

    [Fact]
    public void Price_resolution_never_reads_the_client_tax_exemption()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Teikem.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        foreach (var file in new[] { "PriceListQueries.cs", "SalesCatalogService.cs" })
        {
            var text = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Teikem.Infrastructure", "Services", file));
            Assert.DoesNotContain("ClientTaxExemption", text);
            Assert.DoesNotContain("TaxExemption", text);
        }
    }
}
