using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 182 (C4): recepción maestra — varios documentos del mismo dueño que llegan juntos; cada uno conserva su recibo y se confirman juntos.
public sealed class ReceiptGroupTests
{
    private static async Task<AsnDto> AsnOfClientAsync(ReceivingFixture f, decimal qty)
        => await f.Get<AsnService>().CreateAsync(new AsnCreateRequest(null, f.ClientPublicId, Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, qty) }), default);

    private static ReceiptGroupCreateRequest Docs(params int[] asnIds) => new(asnIds.Select(i => new ReceiptGroupDocumentRequest(i)).ToList(), Carrier: "Transportes Uno", Reference: "BL-1", Note: "Camión de la mañana");

    private static decimal Stock(ReceivingFixture f) => f.Db.Set<InventoryTransaction>().AsNoTracking().ToList().Sum(t => t.Quantity);

    [Fact]
    public void The_selection_needs_two_to_twenty_distinct_documents_each_with_one_origin()
    {
        var guid = Guid.NewGuid();
        Assert.Equal(("documents", ReceiptGroupRules.DocumentsRequired), ReceiptGroupRules.ValidateSelection(new (int?, Guid?)[] { (1, null) }));
        Assert.Equal(("documents", ReceiptGroupRules.TooManyDocuments), ReceiptGroupRules.ValidateSelection(Enumerable.Range(1, 21).Select(i => ((int?)i, (Guid?)null)).ToList()));
        Assert.Equal(("documents[1]", ReceiptGroupRules.DocumentRepeated), ReceiptGroupRules.ValidateSelection(new (int?, Guid?)[] { (1, null), (1, null) }));
        Assert.Equal(("documents[0]", ReceiptGroupRules.DocumentRequired), ReceiptGroupRules.ValidateSelection(new (int?, Guid?)[] { (1, guid), (2, null) }));
        Assert.Equal(("documents[1]", ReceiptGroupRules.DocumentRequired), ReceiptGroupRules.ValidateSelection(new (int?, Guid?)[] { (1, null), (null, null) }));
        Assert.Null(ReceiptGroupRules.ValidateSelection(new (int?, Guid?)[] { (1, null), (null, guid) }));
        Assert.Equal("CANCELLED", ReceiptGroupRules.Status(false, 2, 2));
        Assert.Equal("CONFIRMED", ReceiptGroupRules.Status(true, 2, 2));
        Assert.Equal("OPEN", ReceiptGroupRules.Status(true, 2, 1));
    }

    [Fact]
    public async Task Opening_the_group_creates_one_receipt_per_document_with_the_shared_transport_and_confirming_posts_them_all()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var a1 = await AsnOfClientAsync(f, 4m);
        var a2 = await AsnOfClientAsync(f, 6m);
        var svc = f.Get<ReceiptGroupService>();

        var group = await svc.CreateAsync(Docs(a1.Id, a2.Id), default);
        Assert.Equal(("RCM-00001", "OPEN", 2, 0, 10m, 10m, "Cliente Uno"), (group.Number, group.StatusCode, group.MemberCount, group.ConfirmedCount, group.ExpectedQty, group.ReceivedQty, group.OwnerName));
        Assert.True(group.CanConfirm && group.CanDelete);
        Assert.Equal(new[] { "ASN", "ASN" }, group.Members.Select(m => m.OriginKind));
        Assert.Equal(new[] { a1.Number, a2.Number }, group.Members.Select(m => m.OriginRef));
        // cada documento conserva su recibo de siempre, con el transporte y la referencia compartidos y ligado a la maestra
        var headers = await f.Db.Set<ReceiptHeader>().AsNoTracking().OrderBy(h => h.ReceiptHeaderId).ToListAsync();
        Assert.Equal(2, headers.Count);
        Assert.All(headers, h => Assert.Equal(("Transportes Uno", "BL-1"), (h.Carrier, h.Reference)));
        Assert.Equal(1, headers.Select(h => h.ReceiptGroupId).Distinct().Count());
        Assert.Equal("Camión de la mañana", group.Note);

        var done = await svc.ConfirmAsync(group.PublicId, null, default);
        Assert.Equal(("CONFIRMED", 2, 10m), (done.StatusCode, done.ConfirmedCount, done.ReceivedQty));
        Assert.False(done.CanConfirm);
        Assert.Equal(10m, Stock(f));
        Assert.Equal(AccountingFreeMessage(), (await Assert.ThrowsAsync<StatusRuleException>(() => svc.ConfirmAsync(group.PublicId, null, default))).Message);
        Assert.Equal(ReceiptGroupRules.DeleteHasConfirmed, (await Assert.ThrowsAsync<StatusRuleException>(() => svc.DeleteAsync(group.PublicId, default))).Message);
    }

    private static string AccountingFreeMessage() => ReceiptGroupRules.AlreadyConfirmed;

    [Fact]
    public async Task Deleting_an_open_group_deletes_its_receipts_and_frees_the_documents_for_a_new_one()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var a1 = await AsnOfClientAsync(f, 4m);
        var a2 = await AsnOfClientAsync(f, 6m);
        var svc = f.Get<ReceiptGroupService>();
        var group = await svc.CreateAsync(Docs(a1.Id, a2.Id), default);

        await svc.DeleteAsync(group.PublicId, default);
        var gone = await svc.GetAsync(group.PublicId, default);
        Assert.Equal(("CANCELLED", false), (gone.StatusCode, gone.IsActive));
        Assert.Equal(2, gone.Members.Count());   // la maestra eliminada conserva lo que tuvo
        Assert.All(await f.Db.Set<ReceiptHeader>().AsNoTracking().ToListAsync(), h => Assert.False(h.IsActive));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.DeleteAsync(group.PublicId, default));

        var again = await svc.CreateAsync(Docs(a1.Id, a2.Id), default);
        Assert.Equal(("RCM-00002", "OPEN"), (again.Number, again.StatusCode));
    }

    [Fact]
    public async Task A_document_of_another_owner_a_repeated_one_or_too_few_are_rejected_without_leaving_anything()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var a1 = await AsnOfClientAsync(f, 4m);
        var a2 = await AsnOfClientAsync(f, 6m);
        var svc = f.Get<ReceiptGroupService>();

        Assert.Equal(ReceiptGroupRules.DocumentsRequired, (await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Docs(a1.Id), default))).Message);
        Assert.Equal(ReceiptGroupRules.DocumentRepeated, (await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Docs(a1.Id, a1.Id), default))).Message);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.CreateAsync(Docs(a1.Id, 99999), default));
        // un aviso del cliente y una orden de compra (propia) no se mezclan
        var mixed = new ReceiptGroupCreateRequest(new[] { new ReceiptGroupDocumentRequest(a1.Id), new ReceiptGroupDocumentRequest(PurchaseOrderPublicId: f.PoPublicId) });
        var owner = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(mixed, default));
        Assert.Contains("dueño", owner.Message);
        var noteTooLong = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Docs(a1.Id, a2.Id) with { Note = new string('n', 501) }, default));
        Assert.Equal(ReceiptGroupRules.NoteTooLong, noteTooLong.Message);
        Assert.Empty(await f.Db.Set<ReceiptHeader>().AsNoTracking().ToListAsync());
        Assert.Empty(await f.Db.Set<ReceiptGroup>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task The_list_filters_by_status_and_finds_a_group_by_the_number_of_one_of_its_documents()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var a1 = await AsnOfClientAsync(f, 4m);
        var a2 = await AsnOfClientAsync(f, 6m);
        var a3 = await AsnOfClientAsync(f, 1m);
        var a4 = await AsnOfClientAsync(f, 2m);
        var svc = f.Get<ReceiptGroupService>();
        var open = await svc.CreateAsync(Docs(a1.Id, a2.Id), default);
        var confirmed = await svc.CreateAsync(Docs(a3.Id, a4.Id), default);
        await svc.ConfirmAsync(confirmed.PublicId, null, default);

        Assert.Equal(2, (await svc.ListAsync(new ReceiptGroupQuery(), default)).Total);
        Assert.Equal(open.PublicId, Assert.Single((await svc.ListAsync(new ReceiptGroupQuery(Status: "open"), default)).Items).PublicId);
        Assert.Equal(confirmed.PublicId, Assert.Single((await svc.ListAsync(new ReceiptGroupQuery(Status: "CONFIRMED"), default)).Items).PublicId);
        Assert.Equal(open.PublicId, Assert.Single((await svc.ListAsync(new ReceiptGroupQuery(Search: a2.Number), default)).Items).PublicId);
        Assert.Equal(confirmed.PublicId, Assert.Single((await svc.ListAsync(new ReceiptGroupQuery(Search: confirmed.Members[0].ReceiptNumber), default)).Items).PublicId);
        Assert.Empty((await svc.ListAsync(new ReceiptGroupQuery(Search: "NO-EXISTE"), default)).Items);
        await Assert.ThrowsAsync<ValidationException>(() => svc.ListAsync(new ReceiptGroupQuery(Status: "OTRO"), default));
    }
}
