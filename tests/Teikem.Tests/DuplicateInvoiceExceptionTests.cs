using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 3: 409 de factura repetida (R36). Con el scope fijado (portal) y la orden existente de otro cliente, la respuesta no nombra
/// esa orden ni expone su PublicId (sin oráculo entre clientes).
/// </summary>
public class DuplicateInvoiceExceptionTests
{
    [Fact]
    public void Foreign_duplicate_carries_only_the_invoice_error()
    {
        var ex = new DuplicateInvoiceException("m", confirmable: true, existingOrderNumber: null, existingOrderPublicId: null);
        Assert.Equal(DuplicateInvoiceException.ConfirmableCode, ex.Code);
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(new[] { "clientInvoiceNumber" }, ex.Errors!.Keys.ToArray());
        Assert.Null(ex.ExistingOrderNumber);
        Assert.Null(ex.ExistingOrderPublicId);
    }

    [Fact]
    public void Own_duplicate_carries_the_existing_order()
    {
        var pid = Guid.NewGuid();
        var ex = new DuplicateInvoiceException("m", confirmable: false, "ORD-1", pid);
        Assert.Equal(DuplicateInvoiceException.BlockedCode, ex.Code);
        Assert.Equal(new[] { "m" }, ex.Errors!["clientInvoiceNumber"]);
        Assert.Equal(new[] { "ORD-1" }, ex.Errors!["existingOrderNumber"]);
        Assert.Equal(new[] { pid.ToString() }, ex.Errors!["existingOrderPublicId"]);
    }

    [Fact]
    public void Foreign_messages_do_not_name_the_existing_order()
    {
        foreach (var message in new[]
                 {
                     OrderService.DuplicateInvoiceBlockedForeignMessage("C", "F-1"),
                     OrderService.DuplicateInvoiceConfirmableForeignMessage("C", "F-1"),
                     OrderImportService.DuplicateInvoiceForeignWarning("C", "F-1"),
                 })
        {
            Assert.Contains("F-1", message);
            // "crear la orden de todos modos" se refiere a la nueva; nunca "la orden <número>" ni "en la orden".
            Assert.DoesNotMatch(@"(?i)\bla orden (?!de todos modos)", message);
            Assert.DoesNotContain("en la orden", message);
        }
    }
}
