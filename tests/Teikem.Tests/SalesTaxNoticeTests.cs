using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Tax;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 248 (pieza E) — aviso de exención de IVU al escoger el cliente de una orden de venta (SalesTaxNoticeService): NONE sin exenciones o con TAX apagado;
/// EXPIRED con el texto exacto y la fecha en el formato de la compañía; vencida pero cubierta por otra vigente = NONE; EXPIRING dentro del aviso de la compañía
/// (sin «hasta» no avisa); las exenciones viejas sin documento cubren pero no se muestran; 400 sin cliente y 404 con el de otra compañía; seguridad del endpoint.
/// </summary>
public sealed class SalesTaxNoticeTests
{
    // 27/11/2026 al mediodía de Puerto Rico
    private static readonly TenantClock Clock = new(LocalDay.DefaultZone, () => new DateTime(2026, 11, 27, 16, 0, 0, DateTimeKind.Utc));
    private static readonly DateOnly Today = new(2026, 11, 27);

    private static async Task<WmsFixture> WorldAsync(string dateOrder = TenantFormatRules.DateOrders.DayMonthYear, string thresholds = "30,15,5")
    {
        var f = await WmsFixture.CreateAsync();
        f.SetModules(ModuleKeys.SalesOrders, ModuleKeys.Tax);
        var t = await f.Db.Tenants.AsTracking().SingleAsync(x => x.TenantId == WmsFixture.TenantId);
        t.DateOrder = dateOrder;
        t.DateSeparator = "/";
        t.DocExpiryThresholdDays = thresholds;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return f;
    }

    private static SalesTaxNoticeService Svc(WmsFixture f) => new(f.Db, f.Tenant, f.Get<ModuleService>(), Clock);

    /// <summary>Exención con documento: el documento manda la vigencia y el número (se inserta directo para poder sembrarla ya vencida).</summary>
    private static async Task SeedBackedAsync(WmsFixture f, int clientId, string number, DateOnly from, DateOnly? to, string scope = TaxExemptionScopes.All,
        string? components = null, bool documentActive = true)
    {
        var doc = await TaxTestData.SeedTaxDocumentAsync(f.Db, WmsFixture.TenantId, clientId, number, from, to, isActive: documentActive);
        f.Db.Set<ClientTaxExemption>().Add(new ClientTaxExemption
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = clientId, ExemptionType = "Certificado", CertificateNumber = number, ValidFrom = from,
            ValidTo = to, IsActive = true, Scope = scope, ComponentCodes = components, ClientDocumentId = doc.ClientDocumentId, CreatedAtUtc = DateTime.UtcNow,
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task None_without_exemptions_or_with_the_tax_module_off()
    {
        await using var f = await WorldAsync();
        var c = await f.AddClientAsync("C1");
        Assert.Equal(SalesTaxNoticeLevels.None, (await Svc(f).GetAsync(c.PublicId, default)).Level);

        await SeedBackedAsync(f, c.ClientId, "EX-1", Today.AddYears(-1), Today.AddDays(-3));
        Assert.Equal(SalesTaxNoticeLevels.Expired, (await Svc(f).GetAsync(c.PublicId, default)).Level);
        f.SetModules(ModuleKeys.SalesOrders);
        var off = await Svc(f).GetAsync(c.PublicId, default);
        Assert.Equal(SalesTaxNoticeLevels.None, off.Level);
        Assert.Null(off.Message);
        Assert.Null(off.ValidTo);
    }

    [Fact]
    public async Task Expired_uses_the_exact_text_and_the_company_date_format()
    {
        await using var f = await WorldAsync();
        var c = await f.AddClientAsync("C1");
        await SeedBackedAsync(f, c.ClientId, "EX-20", new DateOnly(2025, 11, 20), new DateOnly(2026, 11, 20));
        var n = await Svc(f).GetAsync(c.PublicId, default);
        Assert.Equal(SalesTaxNoticeLevels.Expired, n.Level);
        Assert.Equal("El certificado de exención de IVU de este cliente venció el 20/11/2026: se cobrará el IVU.", n.Message);
        Assert.Equal(new DateOnly(2026, 11, 20), n.ValidTo);
        Assert.Equal(-7, n.Days);
        Assert.Equal("EX-20", n.CertificateNumber);
        Assert.False(string.IsNullOrWhiteSpace(n.DocumentTypeLabel));

        // otra compañía con MDY: la misma fecha sale 11/20/2026
        await using var g = await WorldAsync(TenantFormatRules.DateOrders.MonthDayYear);
        var d = await g.AddClientAsync("C1");
        await SeedBackedAsync(g, d.ClientId, "EX-20", new DateOnly(2025, 11, 20), new DateOnly(2026, 11, 20));
        Assert.Equal("El certificado de exención de IVU de este cliente venció el 11/20/2026: se cobrará el IVU.", (await Svc(g).GetAsync(d.PublicId, default)).Message);
    }

    [Fact]
    public async Task Expired_but_covered_by_another_valid_exemption_is_none()
    {
        await using var f = await WorldAsync();
        // municipal vencida y renovada con otro certificado municipal vigente
        var c1 = await f.AddClientAsync("C1");
        await SeedBackedAsync(f, c1.ClientId, "MUN-1", Today.AddYears(-1), Today.AddDays(-1), TaxExemptionScopes.Components, "CITY");
        await SeedBackedAsync(f, c1.ClientId, "MUN-2", Today.AddDays(-1), Today.AddYears(1), TaxExemptionScopes.Components, "CITY");
        Assert.Equal(SalesTaxNoticeLevels.None, (await Svc(f).GetAsync(c1.PublicId, default)).Level);

        // total vencida y otra total vigente
        var c2 = await f.AddClientAsync("C2");
        await SeedBackedAsync(f, c2.ClientId, "TOT-1", Today.AddYears(-1), Today.AddDays(-10));
        await SeedBackedAsync(f, c2.ClientId, "TOT-2", Today.AddDays(-10), Today.AddYears(1));
        Assert.Equal(SalesTaxNoticeLevels.None, (await Svc(f).GetAsync(c2.PublicId, default)).Level);

        // total vencida con solo una municipal vigente: SÍ avisa (se cobrará el IVU estatal)
        var c3 = await f.AddClientAsync("C3");
        await SeedBackedAsync(f, c3.ClientId, "TOT-3", Today.AddYears(-1), Today.AddDays(-2));
        await SeedBackedAsync(f, c3.ClientId, "MUN-3", Today.AddDays(-30), Today.AddYears(1), TaxExemptionScopes.Components, "CITY");
        var n = await Svc(f).GetAsync(c3.PublicId, default);
        Assert.Equal(SalesTaxNoticeLevels.Expired, n.Level);
        Assert.Equal("TOT-3", n.CertificateNumber);
    }

    [Fact]
    public async Task Expiring_within_the_company_warning_and_never_without_valid_to()
    {
        await using var f = await WorldAsync();
        var c = await f.AddClientAsync("C1");
        await SeedBackedAsync(f, c.ClientId, "EX-7", Today.AddYears(-1), new DateOnly(2026, 12, 7));
        var n = await Svc(f).GetAsync(c.PublicId, default);
        Assert.Equal(SalesTaxNoticeLevels.Expiring, n.Level);
        Assert.Equal("El certificado de exención de IVU de este cliente vence el 07/12/2026 (en 10 días): después se cobrará el IVU.", n.Message);
        Assert.Equal(10, n.Days);
        Assert.Equal("EX-7", n.CertificateNumber);

        // el borde: el día que vence también avisa (en 0 días)
        var today = await f.AddClientAsync("C0");
        await SeedBackedAsync(f, today.ClientId, "EX-0", Today.AddYears(-1), Today);
        var t0 = await Svc(f).GetAsync(today.PublicId, default);
        Assert.Equal(SalesTaxNoticeLevels.Expiring, t0.Level);
        Assert.Equal(0, t0.Days);

        // más allá del aviso de la compañía (30 días): nada
        var far = await f.AddClientAsync("C2");
        await SeedBackedAsync(f, far.ClientId, "EX-31", Today.AddYears(-1), Today.AddDays(31));
        Assert.Equal(SalesTaxNoticeLevels.None, (await Svc(f).GetAsync(far.PublicId, default)).Level);

        // una exención sin «hasta» no avisa
        var open = await f.AddClientAsync("C3");
        await SeedBackedAsync(f, open.ClientId, "EX-OPEN", Today.AddYears(-1), null);
        Assert.Equal(SalesTaxNoticeLevels.None, (await Svc(f).GetAsync(open.PublicId, default)).Level);

        // la que vence pronto pero otra total la sigue cubriendo después: nada
        var chained = await f.AddClientAsync("C4");
        await SeedBackedAsync(f, chained.ClientId, "OLD", Today.AddYears(-1), Today.AddDays(5));
        await SeedBackedAsync(f, chained.ClientId, "NEW", Today.AddDays(-1), Today.AddYears(1));
        Assert.Equal(SalesTaxNoticeLevels.None, (await Svc(f).GetAsync(chained.PublicId, default)).Level);
    }

    [Fact]
    public async Task Expiring_follows_the_company_thresholds()
    {
        await using var f = await WorldAsync(thresholds: "5,0");
        var c = await f.AddClientAsync("C1");
        await SeedBackedAsync(f, c.ClientId, "EX-10", Today.AddYears(-1), Today.AddDays(10));
        Assert.Equal(SalesTaxNoticeLevels.None, (await Svc(f).GetAsync(c.PublicId, default)).Level);
        var soon = await f.AddClientAsync("C2");
        await SeedBackedAsync(f, soon.ClientId, "EX-5", Today.AddYears(-1), Today.AddDays(5));
        Assert.Equal(SalesTaxNoticeLevels.Expiring, (await Svc(f).GetAsync(soon.PublicId, default)).Level);
    }

    [Fact]
    public async Task Exemptions_without_document_cover_but_are_never_shown()
    {
        await using var f = await WorldAsync();
        // vieja vencida: nada (no se le muestra al vendedor)
        var c1 = await f.AddClientAsync("C1");
        await TaxTestData.SeedLegacyExemptionAsync(f.Db, WmsFixture.TenantId, c1.ClientId, "Revendedor", "VIEJA-1", Today.AddYears(-2), Today.AddDays(-5));
        Assert.Equal(SalesTaxNoticeLevels.None, (await Svc(f).GetAsync(c1.PublicId, default)).Level);

        // vieja por vencer: nada
        var c2 = await f.AddClientAsync("C2");
        await TaxTestData.SeedLegacyExemptionAsync(f.Db, WmsFixture.TenantId, c2.ClientId, "Revendedor", "VIEJA-2", Today.AddYears(-2), Today.AddDays(3));
        Assert.Equal(SalesTaxNoticeLevels.None, (await Svc(f).GetAsync(c2.PublicId, default)).Level);

        // vieja vigente sin «hasta» cubre una total con documento vencida: el cliente sigue exento, nada que avisar
        var c3 = await f.AddClientAsync("C3");
        await TaxTestData.SeedLegacyExemptionAsync(f.Db, WmsFixture.TenantId, c3.ClientId, "Revendedor", "VIEJA-3", Today.AddYears(-2), null);
        await SeedBackedAsync(f, c3.ClientId, "TOT", Today.AddYears(-1), Today.AddDays(-1));
        Assert.Equal(SalesTaxNoticeLevels.None, (await Svc(f).GetAsync(c3.PublicId, default)).Level);

        // vencida con documento junto a una vieja vencida: avisa la del documento (con su número, nunca el de la vieja)
        var c4 = await f.AddClientAsync("C4");
        await TaxTestData.SeedLegacyExemptionAsync(f.Db, WmsFixture.TenantId, c4.ClientId, "Revendedor", "VIEJA-4", Today.AddYears(-2), Today.AddDays(-1));
        await SeedBackedAsync(f, c4.ClientId, "DOC-4", Today.AddYears(-1), Today.AddDays(-3));
        var n = await Svc(f).GetAsync(c4.PublicId, default);
        Assert.Equal(SalesTaxNoticeLevels.Expired, n.Level);
        Assert.Equal("DOC-4", n.CertificateNumber);
    }

    [Fact]
    public async Task An_exemption_whose_document_was_removed_does_not_count()
    {
        await using var f = await WorldAsync();
        var c = await f.AddClientAsync("C1");
        await SeedBackedAsync(f, c.ClientId, "GONE", Today.AddYears(-1), Today.AddDays(-2), documentActive: false);
        Assert.Equal(SalesTaxNoticeLevels.None, (await Svc(f).GetAsync(c.PublicId, default)).Level);
    }

    [Fact]
    public async Task Without_client_is_400_and_another_company_client_is_404()
    {
        await using var f = await WorldAsync();
        var bad = await Assert.ThrowsAsync<ValidationException>(() => Svc(f).GetAsync(null, default));
        Assert.Equal("Indique el cliente.", bad.Message);
        Assert.Equal(400, bad.StatusCode);
        await Assert.ThrowsAsync<ValidationException>(() => Svc(f).GetAsync(Guid.Empty, default));

        var other = await f.AddClientAsync("OT", WmsFixture.OtherTenantId);
        var nf = await Assert.ThrowsAsync<NotFoundException>(() => Svc(f).GetAsync(other.PublicId, default));
        Assert.Equal(404, nf.StatusCode);
        await Assert.ThrowsAsync<NotFoundException>(() => Svc(f).GetAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public void The_endpoint_requires_sales_orders_and_sales_create_or_view()
    {
        var controller = typeof(SalesTaxNoticeController);
        Assert.Equal("api/v1/sales-orders", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(ModuleKeys.SalesOrders, controller.GetCustomAttribute<RequireModuleAttribute>()!.ModuleKey);
        var action = controller.GetMethod(nameof(SalesTaxNoticeController.Get))!;
        Assert.Equal("tax-notice", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.SalesCreate + "|" + PermissionCatalog.SalesView,
            action.GetCustomAttribute<RequirePermissionAttribute>()!.Policy);
        var verbs = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>()).ToList();
        Assert.Single(verbs);
    }
}
