using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 149 — «el recibo exige documento»: reglas puras y el servicio de recibos sobre el fixture de recepción.</summary>
public sealed class ReceiptRequirementTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("FREE", false)]
    [InlineData("raro", false)]
    [InlineData("any_document", true)]
    [InlineData(" ASN ", true)]
    [InlineData("PURCHASE_ORDER", true)]
    public void Requires_only_with_a_valid_non_free_value(string? value, bool requires)
        => Assert.Equal(requires, ReceiptRequirementRules.Requires(value));

    [Fact]
    public void Violation_follows_the_kind_of_document()
    {
        Assert.Null(ReceiptRequirementRules.Violation("FREE", false, false));
        Assert.Equal(ReceiptRequirementRules.NoDocument("ANY_DOCUMENT"), ReceiptRequirementRules.Violation("ANY_DOCUMENT", false, false));
        Assert.Null(ReceiptRequirementRules.Violation("ANY_DOCUMENT", true, true));
        Assert.Null(ReceiptRequirementRules.Violation("ANY_DOCUMENT", true, false));
        Assert.Null(ReceiptRequirementRules.Violation("ASN", true, false));
        Assert.Equal(ReceiptRequirementRules.WrongKindPurchaseOrder, ReceiptRequirementRules.Violation("ASN", true, true));
        Assert.Null(ReceiptRequirementRules.Violation("PURCHASE_ORDER", true, true));
        Assert.Equal(ReceiptRequirementRules.WrongKindAsn, ReceiptRequirementRules.Violation("PURCHASE_ORDER", true, false));
        Assert.Contains("aviso de llegada", ReceiptRequirementRules.NoDocument("ASN"));
        Assert.Contains("orden de compra", ReceiptRequirementRules.NoDocument("PURCHASE_ORDER"));
    }

    private static async Task SetAsync(ReceivingFixture f, string requirement)
    {
        var tenant = await f.Db.Tenants.SingleAsync(t => t.TenantId == ReceivingFixture.TenantId);
        tenant.ReceiptRequirement = requirement;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task A_blind_receipt_is_blocked_when_the_company_requires_a_document_and_allowed_when_free()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(Type: "BLIND"), default);   // libre: como siempre
        Assert.NotNull(created.Header);

        await SetAsync(f, "ANY_DOCUMENT");
        var ex = await Assert.ThrowsAsync<ConflictException>(() => receipts.CreateAsync(new ReceiptCreateRequest(Type: "BLIND"), default));
        Assert.Equal(ReceiptRequirementRules.NoDocument("ANY_DOCUMENT"), ex.Message);
    }

    [Fact]
    public async Task Only_a_client_asn_when_asn_is_required_and_only_a_purchase_order_when_that_is_required()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var asn = await f.Get<AsnService>().CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 5m) }), default);
        var receipts = f.Get<ReceiptService>();

        await SetAsync(f, "ASN");
        Assert.NotNull((await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id), default)).Header);   // aviso de un cliente: sirve
        var po = await Assert.ThrowsAsync<ConflictException>(() => receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId), default));
        Assert.Equal(ReceiptRequirementRules.WrongKindPurchaseOrder, po.Message);

        await SetAsync(f, "PURCHASE_ORDER");
        var wrong = await Assert.ThrowsAsync<ConflictException>(() => receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id), default));
        Assert.Equal(ReceiptRequirementRules.WrongKindAsn, wrong.Message);
        Assert.NotNull((await receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId), default)).Header);
    }
}
