using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Xunit;

namespace Teikem.Tests;

// Lote 222 (P2 del pedido de vendedores): el cliente que entra por un pedido — alta mínima con clients.create_basic (nace PROSPECTO), se le solicita (reserva) pero no se
// despacha; aprobar (→ ACTIVO con límite y término) y rechazar (→ SUSPENDIDO y cancela sus órdenes abiertas), todo por StatusService y con constancia.
public sealed class ClientProspectTests
{
    private static async Task<Client> RowAsync(SalesVendorWorld x, Guid publicId) => await x.F.Db.Clients.AsNoTracking().SingleAsync(c => c.PublicId == publicId);

    [Fact]
    public async Task The_quick_sign_up_creates_a_prospect_with_contact_phones_email_suri_addresses_and_payment()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        x.F.SetPermissions(PermissionCatalog.ClientsCreateBasic, PermissionCatalog.ClientsRead);   // el vendedor: sin clients.create ni clients.update
        var dto = await x.Prospects.CreateBasicAsync(SalesVendorWorld.BasicRequest(), default);

        Assert.Equal((ClientStatuses.Prospect, "Farmacia Nueva", "Farmacia Nueva LLC", "NET30", "SURI-12345"), (dto.Status, dto.Name, dto.LegalName, dto.PaymentTerm, dto.MerchantRegistration));
        Assert.Equal(("Calle Sol 10", "Ponce", "00716"), (dto.PhysicalAddress!.Line1, dto.PhysicalAddress.City, dto.PhysicalAddress.PostalCode));
        Assert.Equal("PO Box 55", dto.PostalAddress!.Line1);
        var contact = Assert.Single(dto.Contacts);
        Assert.Equal(("Ana Rivera", true), (contact.FullName, contact.IsPrimary));
        Assert.Equal(3, contact.ContactPoints.Count);   // teléfono (con extensión), teléfono 2 y correo
        Assert.Contains(contact.ContactPoints, p => p.Value == "ana@farmacia.com");
        // nace en PROSPECTO con historial (de nada a Prospecto)
        var row = await RowAsync(x, dto.PublicId);
        var history = await x.F.Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityId == row.ClientId).ToListAsync();
        var born = Assert.Single(history);
        Assert.Equal((null, x.ProspectStatusId), (born.FromStatusCodeId, born.ToStatusCodeId));
    }

    [Fact]
    public async Task The_quick_sign_up_validates_with_the_exact_messages_before_creating_anything()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        async Task<ValidationException> Bad(ClientBasicCreateRequest r) => await Assert.ThrowsAsync<ValidationException>(() => x.Prospects.CreateBasicAsync(r, default));
        var ok = SalesVendorWorld.BasicRequest();
        Assert.Equal(ClientProspectRules.PhoneTenDigits, (await Bad(ok with { Phone = "787-555-1234" })).Message);
        Assert.Equal(ClientProspectRules.ContactRequired, (await Bad(ok with { ContactName = " " })).Message);
        Assert.Equal(ClientProspectRules.AddressRequired, (await Bad(ok with { PhysicalAddress = null })).Message);
        Assert.Equal(ClientProspectRules.ExtensionInvalid, (await Bad(ok with { PhoneExtension = "ext 5" })).Message);
        Assert.Equal("Correo inválido.", (await Bad(ok with { Email = "ana@" })).Message);
        Assert.True((await Bad(ok with { PaymentTerm = "NET999" })).Errors!.ContainsKey("paymentTerm"));
        Assert.Empty(await x.F.Db.Clients.AsNoTracking().Where(c => c.Name == "Farmacia Nueva").ToListAsync());
    }

    [Fact]
    public async Task A_prospect_order_is_requested_and_reserved_but_not_dispatched_until_approved()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var p = await x.ProductAsync("MED", 10m);
        var dto = await x.Prospects.CreateBasicAsync(SalesVendorWorld.BasicRequest(), default);
        var client = await RowAsync(x, dto.PublicId);

        var d = await x.DraftAsync(client, p, 3m);
        Assert.True(d.Terms!.ClientIsProspect);
        var r = await x.Sales.RequestAsync(d.PublicId, null, default);
        Assert.Equal((SalesOrderStatuses.Requested, true, 3m), (r.Status, r.Terms!.AwaitingClientApproval, r.Lines[0].QtyReserved));
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Sales.DispatchAsync(r.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(r.Lines[0].Id, 3m) }), default));
        Assert.Equal(SalesOrderExtraRules.AwaitingClientApproval, ex.Message);

        // aprobar: → ACTIVO con su límite y término, constancia en el historial, y la orden ya se despacha
        var approved = await x.Prospects.ApproveAsync(dto.PublicId, new ClientApproveRequest(2500m, "NET15", "SURI verificado"), default);
        Assert.Equal((ClientStatuses.Active, 2500m, "NET15"), (approved.Status, approved.CreditLimit, approved.PaymentTerm));
        var last = await x.F.Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityId == client.ClientId).OrderBy(h => h.EntityStatusHistoryId).LastAsync();
        Assert.Contains("límite de crédito $2,500.00", last.Comment);
        Assert.Contains("SURI verificado", last.Comment);
        var after = await x.Sales.GetAsync(r.PublicId, default);
        Assert.False(after.Terms!.AwaitingClientApproval);
        var shipped = await x.Sales.DispatchAsync(r.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(r.Lines[0].Id, 3m) }), default);
        Assert.Equal(SalesOrderStatuses.Shipped, shipped.Order.Status);

        await Assert.ThrowsAsync<StatusRuleException>(() => x.Prospects.ApproveAsync(dto.PublicId, null, default));   // ya no es prospecto
    }

    [Fact]
    public async Task Rejecting_needs_a_comment_cancels_the_open_orders_releasing_stock_and_suspends_the_client()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var p = await x.ProductAsync("MED", 10m, stock: 10m);
        var dto = await x.Prospects.CreateBasicAsync(SalesVendorWorld.BasicRequest(), default);
        var client = await RowAsync(x, dto.PublicId);
        var draft = await x.DraftAsync(client, p, 1m);
        var requested = await x.Sales.RequestAsync((await x.DraftAsync(client, p, 4m)).PublicId, null, default);

        var noComment = await Assert.ThrowsAsync<ValidationException>(() => x.Prospects.RejectAsync(dto.PublicId, new ClientRejectRequest("  "), default));
        Assert.Equal(ClientProspectRules.RejectNeedsComment, noComment.Message);

        var result = await x.Prospects.RejectAsync(dto.PublicId, new ClientRejectRequest("El SURI no es válido."), default);
        Assert.Equal(ClientStatuses.Suspended, result.Client.Status);
        Assert.Equal(new[] { draft.Number, requested.Number }, result.CancelledOrders);
        Assert.Equal(SalesOrderStatuses.Cancelled, (await x.Sales.GetAsync(draft.PublicId, default)).Status);
        Assert.Equal(SalesOrderStatuses.Cancelled, (await x.Sales.GetAsync(requested.PublicId, default)).Status);
        var balance = await x.F.Db.StockBalances.AsNoTracking().SingleAsync(b => b.ProductId == p.ProductId);
        Assert.Equal(0m, balance.QtyReserved);   // lo reservado se liberó
        var last = await x.F.Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityId == client.ClientId).OrderBy(h => h.EntityStatusHistoryId).LastAsync();
        Assert.Contains("El SURI no es válido.", last.Comment);

        // un cliente suspendido no compra
        var again = await x.DraftAsync(await RowAsync(x, dto.PublicId), p, 1m);
        var blocked = await Assert.ThrowsAsync<StatusRuleException>(() => x.Sales.RequestAsync(again.PublicId, null, default));
        Assert.Equal(SalesOrderExtraRules.ClientSuspended, blocked.Message);
        await Assert.ThrowsAsync<StatusRuleException>(() => x.Prospects.RejectAsync(dto.PublicId, new ClientRejectRequest("otra vez"), default));
    }

    [Fact]
    public async Task A_prospect_leaves_prospect_only_by_approval_and_the_client_signature_rule_blocks_the_request()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var dto = await x.Prospects.CreateBasicAsync(SalesVendorWorld.BasicRequest(), default);
        x.F.SetPermissions(PermissionCatalog.ClientsUpdate, PermissionCatalog.ClientsRead);
        var forbidden = await Assert.ThrowsAsync<ForbiddenException>(() => x.Clients.TransitionStatusAsync(dto.PublicId, new StatusChangeRequest(ClientStatuses.Active, null), default));
        Assert.Equal(ClientProspectRules.ProspectLeavesByApproval, forbidden.Message);

        // «Exige la firma de quien autoriza el pedido»: sin nombre y firma no se solicita; con ellos sí
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("FIRMA", r => r.RequireOrderSignature = true);
        var d = await x.DraftAsync(c, p, 1m);
        Assert.True(d.Terms!.ClientRequiresSignature);
        var missing = await Assert.ThrowsAsync<ValidationException>(() => x.Sales.RequestAsync(d.PublicId, null, default));
        Assert.Equal(SalesOrderExtraRules.SignatureRequired, missing.Message);
        var order = await x.F.Db.Set<SalesOrder>().AsTracking().SingleAsync(o => o.PublicId == d.PublicId);
        var file = new Teikem.Domain.Files.StoredFile
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, OwnerEntityLookupId = x.Lookup(LookupDomains.EntityType, SalesCodes.EntityType), OwnerId = order.SalesOrderId,
            Kind = Teikem.Domain.Files.StoredFileCodes.KindSignature, FileName = "firma.png", ContentType = "image/png", Sha256 = "x", StorageKey = "k",
        };
        x.F.Db.Set<Teikem.Domain.Files.StoredFile>().Add(file);
        await x.F.Db.SaveChangesAsync();
        (order.AuthorizedByName, order.SignatureFileId) = ("Luis Pérez", file.StoredFileId);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var ok = await x.Sales.RequestAsync(d.PublicId, null, default);
        Assert.Equal((SalesOrderStatuses.Requested, file.PublicId), (ok.Status, ok.Terms!.SignatureFilePublicId));
    }

    [Fact]
    public async Task The_profile_keeps_the_suri_the_shipping_rule_and_the_order_signature_flag()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var c = await x.ClientAsync("C1");
        var own = await x.Clients.UpdateProfileAsync(c.PublicId, new ClientProfileUpdateRequest(null, null, null, null, null, null, false, null,
            MerchantRegistration: "SURI-1", ShippingMode: "OWN", ShippingCharge: 8m, ShippingFreeFrom: 75m, RequireOrderSignature: true), default);
        Assert.Equal(("SURI-1", ClientShippingModes.Own, 8m, 75m, true), (own.MerchantRegistration, own.ShippingMode, own.ShippingCharge, own.ShippingFreeFrom, own.RequireOrderSignature));
        var company = await x.Clients.UpdateProfileAsync(c.PublicId, new ClientProfileUpdateRequest(null, null, null, null, null, null, false, null, ShippingMode: "COMPANY"), default);
        Assert.Equal(((string?)null, (decimal?)null, (decimal?)null), (company.ShippingMode, company.ShippingCharge, company.ShippingFreeFrom));   // vuelve a la de la compañía, sin copia
        var ex = await Assert.ThrowsAsync<ValidationException>(() => x.Clients.UpdateProfileAsync(c.PublicId,
            new ClientProfileUpdateRequest(null, null, null, null, null, null, false, null, ShippingMode: "OWN"), default));
        Assert.Equal(ShippingRules.OwnNeedsCharge, ex.Message);
        await Assert.ThrowsAsync<ValidationException>(() => x.Clients.UpdateProfileAsync(c.PublicId,
            new ClientProfileUpdateRequest(null, null, null, null, null, null, false, null, ShippingMode: "GRATIS"), default));
    }
}
