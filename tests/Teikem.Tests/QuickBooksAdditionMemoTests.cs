using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 249 (P6): el MEMO de la factura en el IIF de QuickBooks lleva la referencia de la orden de ADICIÓN («OV-… (adición de OV-… · FAC-…)», con las facturas no anuladas de la madre
// anteriores a esta) y InvoiceDto.Complements trae esos números. La factura complementaria se exporta como una factura más.
public sealed class QuickBooksAdditionMemoTests
{
    private static Task<SalesVendorWorld> WorldAsync() => SalesVendorWorld.CreateAsync(s => s.AddSingleton<QuickBooksService>());

    private static string Text(QuickBooksExportDto dto)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252).GetString(Convert.FromBase64String(dto.ContentBase64));
    }

    /// <summary>El MEMO (columna 8) del renglón TRNS de la factura `number`.</summary>
    private static string MemoOf(string iif, string number)
        => iif.Split("\r\n").Select(l => l.Split('\t')).Single(c => c[0] == "TRNS" && c[7] == number)[8];

    /// <summary>Una orden en borrador del cliente (madre si parentId es null) y una factura de `status` con su línea de producto.</summary>
    private static async Task<(SalesOrder Order, Invoice Invoice)> OrderAndInvoiceAsync(SalesVendorWorld x, Client c, Product p, string invoiceNumber, int? parentId = null,
        string status = InvoiceStatuses.Issued)
    {
        var draft = await x.Sales.CreateAsync(new SalesOrderRequest(WarehousePublicId: x.W.PublicId, ClientPublicId: c.PublicId, Lines: new[] { new SalesOrderLineRequest(p.PublicId, 2m) }), default);
        var so = await x.F.Db.Set<SalesOrder>().AsTracking().SingleAsync(o => o.PublicId == draft.PublicId);
        so.ParentSalesOrderId = parentId;
        await x.F.Db.SaveChangesAsync();
        var line = await x.F.Db.Set<SalesOrderLine>().AsNoTracking().SingleAsync(l => l.SalesOrderId == so.SalesOrderId);
        var invoice = new Invoice
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = c.ClientId, InvoiceNumber = invoiceNumber, StatusCodeId = x.F.StatusId(BillingCodes.InvoiceStatusDomain, status),
            IssueDate = new DateOnly(2026, 11, 2), DueDate = new DateOnly(2026, 12, 2), CurrencyLookupId = 1, IsActive = true, Subtotal = 20m,
        };
        invoice.Lines.Add(new InvoiceLine { ProductId = p.ProductId, ChargeTypeLookupId = x.Lookup(BillingCodes.ChargeTypeDomain, BillingCodes.ProductSale), Description = p.Sku, Quantity = 2m,
            UnitAmount = 10m, SalesOrderLineId = line.SalesOrderLineId });
        x.F.Db.Set<Invoice>().Add(invoice);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return (so, invoice);
    }

    [Fact]
    public async Task The_iif_memo_of_the_complementary_invoice_names_the_mother_and_its_invoices()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var (mother, _) = await OrderAndInvoiceAsync(x, c, p, "FAC-1");
        await OrderAndInvoiceAsync(x, c, p, "FAC-VOID", status: InvoiceStatuses.Void);   // otra orden, anulada: no estorba
        var (addition, _) = await OrderAndInvoiceAsync(x, c, p, "FAC-2", mother.SalesOrderId);

        var iif = Text(await x.F.Get<QuickBooksService>().ExportAsync(new QuickBooksExportRequest(), default));
        Assert.Equal(mother.Number, MemoOf(iif, "FAC-1"));   // la de la madre: solo su número, como siempre
        Assert.Equal($"{addition.Number} (adición de {mother.Number} · FAC-1)", MemoOf(iif, "FAC-2"));
        // la complementaria es una factura más: sus totales y su línea no cambian
        var pending = await x.F.Get<QuickBooksService>().GetPendingAsync(default);
        Assert.Equal(new[] { "FAC-1", "FAC-2" }, pending.Invoices.Select(i => i.Number).OrderBy(n => n));
        Assert.Equal(new[] { addition.Number }, pending.Invoices.Single(i => i.Number == "FAC-2").OrderNumbers);
    }

    [Fact]
    public async Task A_voided_mother_invoice_does_not_appear_and_without_invoices_only_the_mother_is_named()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var draft = await x.Sales.CreateAsync(new SalesOrderRequest(WarehousePublicId: x.W.PublicId, ClientPublicId: c.PublicId, Lines: new[] { new SalesOrderLineRequest(p.PublicId, 2m) }), default);
        var mother = await x.F.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(o => o.PublicId == draft.PublicId);
        // la factura de la madre está anulada (ligada a su línea)
        var motherLine = await x.F.Db.Set<SalesOrderLine>().AsNoTracking().SingleAsync(l => l.SalesOrderId == mother.SalesOrderId);
        var voided = new Invoice
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = c.ClientId, InvoiceNumber = "FAC-V", StatusCodeId = x.F.StatusId(BillingCodes.InvoiceStatusDomain, InvoiceStatuses.Void),
            CurrencyLookupId = 1, IsActive = true,
        };
        voided.Lines.Add(new InvoiceLine { ProductId = p.ProductId, ChargeTypeLookupId = x.Lookup(BillingCodes.ChargeTypeDomain, BillingCodes.ProductSale), Description = p.Sku, Quantity = 2m,
            UnitAmount = 10m, SalesOrderLineId = motherLine.SalesOrderLineId });
        x.F.Db.Set<Invoice>().Add(voided);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var (addition, inv) = await OrderAndInvoiceAsync(x, c, p, "FAC-3", mother.SalesOrderId);

        var iif = Text(await x.F.Get<QuickBooksService>().ExportAsync(new QuickBooksExportRequest(), default));
        Assert.Equal($"{addition.Number} (adición de {mother.Number})", MemoOf(iif, "FAC-3"));
        Assert.Empty((await x.F.Get<BillingService>().GetInvoiceAsync(inv.PublicId, default)).Complements!);
    }

    [Fact]
    public async Task InvoiceDto_complements_brings_the_mother_invoice_numbers()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var (mother, first) = await OrderAndInvoiceAsync(x, c, p, "FAC-10");
        var (_, addInv) = await OrderAndInvoiceAsync(x, c, p, "FAC-11", mother.SalesOrderId);
        var billing = x.F.Get<BillingService>();
        Assert.Equal(new[] { "FAC-10" }, (await billing.GetInvoiceAsync(addInv.PublicId, default)).Complements);
        Assert.Empty((await billing.GetInvoiceAsync(first.PublicId, default)).Complements!);
    }
}
