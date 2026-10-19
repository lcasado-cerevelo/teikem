using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 119 (P10) — las facturas del cliente en su portal: solo las de su cliente, nunca borradores, con lo pagado y el saldo, sin pagos anulados; una factura de otro cliente, un borrador o una inexistente responden 404 igual; exige el
/// módulo BILLING encendido. Solo lectura: el servicio no tiene ninguna operación que cambie una factura.
/// </summary>
public class PortalInvoiceServiceTests
{
    private static async Task<Invoice> InvoiceAsync(WmsFixture f, int clientId, string number, string status, decimal subtotal, decimal paid = 0m, bool voidedPayment = false)
    {
        var inv = new Invoice
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = clientId, InvoiceNumber = number, StatusCodeId = f.StatusId(BillingCodes.InvoiceStatusDomain, status),
            IssueDate = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 31), CurrencyLookupId = 1, Subtotal = subtotal, TaxAmount = Math.Round(subtotal * 0.1m, 2), IsActive = true, Notes = "nota interna",
        };
        inv.Lines.Add(new InvoiceLine { ChargeTypeLookupId = 1, Description = "Entregas de octubre", Quantity = 2m, UnitAmount = subtotal / 2m, TaxAmount = Math.Round(subtotal * 0.1m, 2), AdjustmentNote = "ajuste interno" });
        f.Db.Set<Invoice>().Add(inv);
        await f.Db.SaveChangesAsync();
        if (paid > 0m) f.Db.Set<Payment>().Add(new Payment { TenantId = WmsFixture.TenantId, InvoiceId = inv.InvoiceId, PaymentMethodLookupId = f.LookupId(BillingCodes.PaymentMethodDomain, "CASH"), Amount = paid, PaidAtUtc = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc) });
        if (voidedPayment) f.Db.Set<Payment>().Add(new Payment { TenantId = WmsFixture.TenantId, InvoiceId = inv.InvoiceId, PaymentMethodLookupId = f.LookupId(BillingCodes.PaymentMethodDomain, "CASH"), Amount = 999m, PaidAtUtc = DateTime.UtcNow, VoidedAtUtc = DateTime.UtcNow, VoidReason = "error" });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return inv;
    }

    private static PortalInvoiceService PortalOf(WmsFixture f, int clientId)
    {
        f.Tenant.IsPortal = true;
        f.Tenant.PortalClientId = clientId;
        f.Tenant.ScopedClientIds = new[] { clientId };   // lo que hace TenantContextMiddleware con el claim cid
        return new PortalInvoiceService(f.Db, f.Tenant, f.Lookups, f.Get<ModuleService>());
    }

    private static async Task<(WmsFixture F, int C1, int C2)> WorldAsync(params string[] modules)
    {
        var f = await WmsFixture.CreateAsync();
        f.SetModules(modules);
        var c1 = await f.AddClientAsync("C1");
        var c2 = await f.AddClientAsync("C2");
        return (f, c1.ClientId, c2.ClientId);
    }

    [Fact]
    public async Task The_list_has_only_my_clients_invoices_without_drafts_with_paid_and_balance()
    {
        var (f, c1, c2) = await WorldAsync(ModuleKeys.ClientPortal, ModuleKeys.Billing);
        await using var _ = f;
        await InvoiceAsync(f, c1, "FAC-1", InvoiceStatuses.Issued, 100m, paid: 30m, voidedPayment: true);
        await InvoiceAsync(f, c1, "FAC-2", InvoiceStatuses.Draft, 50m);
        await InvoiceAsync(f, c1, "FAC-3", InvoiceStatuses.Paid, 10m, paid: 11m);
        await InvoiceAsync(f, c2, "FAC-9", InvoiceStatuses.Issued, 70m);

        var page = await PortalOf(f, c1).ListAsync(null, 0, 50, default);

        Assert.Equal(new[] { "FAC-1", "FAC-3" }, page.Items.Select(i => i.Number).OrderBy(n => n));          // sin el borrador y sin la factura del otro cliente
        Assert.Equal(2, page.Total);
        var one = page.Items.Single(i => i.Number == "FAC-1");
        Assert.Equal((110m, 30m, 80m), (one.Total, one.Paid, one.Balance));                                     // el pago anulado no cuenta
        Assert.Single((await PortalOf(f, c1).ListAsync(InvoiceStatuses.Paid, 0, 50, default)).Items);
        var other = await PortalOf(f, c2).ListAsync(null, 0, 50, default);
        Assert.Equal(new[] { "FAC-9" }, other.Items.Select(i => i.Number));
    }

    [Fact]
    public async Task One_invoice_carries_lines_and_payments_but_no_internal_notes_and_hides_other_clients_and_drafts()
    {
        var (f, c1, c2) = await WorldAsync(ModuleKeys.ClientPortal, ModuleKeys.Billing);
        await using var _ = f;
        var mine = await InvoiceAsync(f, c1, "FAC-1", InvoiceStatuses.Issued, 100m, paid: 30m, voidedPayment: true);
        var draft = await InvoiceAsync(f, c1, "FAC-2", InvoiceStatuses.Draft, 50m);
        var foreign = await InvoiceAsync(f, c2, "FAC-9", InvoiceStatuses.Issued, 70m);
        var portal = PortalOf(f, c1);

        var dto = await portal.GetAsync(mine.PublicId, default);
        Assert.Equal("FAC-1", dto.Number);
        Assert.Equal((100m, 10m, 110m, 30m, 80m), (dto.Subtotal, dto.TaxAmount, dto.Total, dto.Paid, dto.Balance));
        Assert.Equal("Entregas de octubre", Assert.Single(dto.Lines).Description);
        Assert.Equal(30m, Assert.Single(dto.Payments).Amount);                                                  // el pago anulado no aparece
        var shape = typeof(PortalInvoiceDto).GetProperties().Select(p => p.Name).Concat(typeof(PortalInvoiceLineDto).GetProperties().Select(p => p.Name)).ToList();
        Assert.DoesNotContain(shape, n => n is "Notes" or "AdjustmentNote" or "BillingRunId" or "RunId" or "OriginalUnitAmount");

        var a = await Assert.ThrowsAsync<NotFoundException>(() => portal.GetAsync(foreign.PublicId, default));
        var b = await Assert.ThrowsAsync<NotFoundException>(() => portal.GetAsync(draft.PublicId, default));
        var c = await Assert.ThrowsAsync<NotFoundException>(() => portal.GetAsync(Guid.NewGuid(), default));
        Assert.All(new[] { a, b, c }, e => Assert.Equal(404, e.StatusCode));
        Assert.Equal(a.Message, c.Message);                                                                     // sin oráculo
    }

    [Fact]
    public async Task Without_the_billing_module_the_portal_has_no_invoices()
    {
        var (f, c1, _) = await WorldAsync(ModuleKeys.ClientPortal);
        await using var _ = f;
        var mine = await InvoiceAsync(f, c1, "FAC-1", InvoiceStatuses.Issued, 100m);
        var portal = PortalOf(f, c1);
        await Assert.ThrowsAnyAsync<Exception>(() => portal.ListAsync(null, 0, 50, default));
        await Assert.ThrowsAnyAsync<Exception>(() => portal.GetAsync(mine.PublicId, default));
    }

    [Fact]
    public void The_service_is_read_only() => Assert.Equal(new[] { "GetAsync", "ListAsync" }, typeof(PortalInvoiceService).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly).Select(m => m.Name).OrderBy(n => n));
}
