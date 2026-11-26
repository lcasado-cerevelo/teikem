using System.Text;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Files;
using Teikem.Domain.Tax;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 222 (P2 del pedido de vendedores): documentos del cliente (registro de comerciante, certificado de revendedor) — StoredFile cifrado con su tipo, quién los sube
// (clients.update, o clients.create_basic mientras es prospecto), validaciones y baja lógica.
public sealed class ClientDocumentTests
{
    [Fact]
    public async Task The_seller_uploads_the_prospect_documents_and_lists_them_with_their_type()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var prospect = await x.Prospects.CreateBasicAsync(SalesVendorWorld.BasicRequest(), default);
        x.F.SetPermissions(PermissionCatalog.ClientsCreateBasic, PermissionCatalog.ClientsRead);

        var doc = await x.Documents.UploadAsync(prospect.PublicId, "merchant_registration", "Foto del SURI", "suri.png", "image/png", SalesVendorWorld.Png, default);
        Assert.Equal((ClientDocumentCodes.MerchantRegistration, "suri.png", "image/png", "Foto del SURI"), (doc.DocumentType, doc.FileName, doc.ContentType, doc.Notes));
        await x.Documents.UploadAsync(prospect.PublicId, ClientDocumentCodes.ResellerCertificate, null, "cert.png", "image/png", SalesVendorWorld.Png, default);

        var list = await x.Documents.ListAsync(prospect.PublicId, default);
        Assert.Equal(new[] { ClientDocumentCodes.MerchantRegistration, ClientDocumentCodes.ResellerCertificate }, list.Select(d => d.DocumentType));
        // el archivo es un StoredFile DOCUMENT del cliente, cifrado
        var file = await x.F.Db.Set<StoredFile>().AsNoTracking().SingleAsync(f => f.PublicId == doc.FilePublicId);
        Assert.Equal((StoredFileCodes.KindDocument, true), (file.Kind, file.IsEncrypted));
        var content = await x.F.Get<Teikem.Infrastructure.Services.StoredFileService>().GetContentAsync(doc.FilePublicId, default);   // se ve con clients.read
        Assert.Equal(SalesVendorWorld.Png, content.Bytes);
    }

    [Fact]
    public async Task Without_clients_update_only_a_prospect_takes_documents_and_bad_files_are_rejected()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var active = await x.ClientAsync("C1");
        x.F.SetPermissions(PermissionCatalog.ClientsCreateBasic, PermissionCatalog.ClientsRead);
        var forbidden = await Assert.ThrowsAsync<ForbiddenException>(() => x.Documents.UploadAsync(active.PublicId, "OTHER", null, "a.png", "image/png", SalesVendorWorld.Png, default));
        Assert.Equal(ClientProspectRules.DocumentNeedsPermission, forbidden.Message);

        x.F.SetPermissions(PermissionCatalog.ClientsUpdate, PermissionCatalog.ClientsRead);
        var badType = await Assert.ThrowsAsync<ValidationException>(() => x.Documents.UploadAsync(active.PublicId, "LICENCIA", null, "a.png", "image/png", SalesVendorWorld.Png, default));
        Assert.Equal(ClientDocumentRules.TypeInvalid, badType.Message);   // Lote 248: el tipo se valida contra el catálogo
        var text = await Assert.ThrowsAsync<ValidationException>(() => x.Documents.UploadAsync(active.PublicId, "OTHER", null, "a.txt", "text/plain", Encoding.UTF8.GetBytes("no es imagen"), default));
        Assert.Equal(StoredFileRules.TypeNotAllowed, text.Message);
        var longNote = await Assert.ThrowsAsync<ValidationException>(() => x.Documents.UploadAsync(active.PublicId, "OTHER", new string('n', 301), "a.png", "image/png", SalesVendorWorld.Png, default));
        Assert.Equal(ClientProspectRules.DocumentNotesTooLong, longNote.Message);
        Assert.Empty(await x.F.Db.Set<ClientDocument>().AsNoTracking().ToListAsync());

        var ok = await x.Documents.UploadAsync(active.PublicId, "OTHER", null, "a.png", "image/png", SalesVendorWorld.Png, default);
        Assert.Equal(ClientDocumentCodes.Other, ok.DocumentType);
    }

    [Fact]
    public async Task Removing_a_document_is_logical_and_takes_its_file_out_of_view()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var c = await x.ClientAsync("C1");
        var doc = await x.Documents.UploadAsync(c.PublicId, "RESELLER_CERTIFICATE", null, "cert.png", "image/png", SalesVendorWorld.Png, default);
        await x.Documents.RemoveAsync(c.PublicId, doc.PublicId, default);
        Assert.Empty(await x.Documents.ListAsync(c.PublicId, default));
        var row = await x.F.Db.Set<ClientDocument>().AsNoTracking().SingleAsync();
        Assert.False(row.IsActive);   // nunca DELETE
        Assert.False((await x.F.Db.Set<StoredFile>().AsNoTracking().SingleAsync(f => f.PublicId == doc.FilePublicId)).IsActive);
        await Assert.ThrowsAsync<NotFoundException>(() => x.Documents.RemoveAsync(c.PublicId, doc.PublicId, default));
        // otro cliente no alcanza el documento
        var other = await x.ClientAsync("C2");
        await Assert.ThrowsAsync<NotFoundException>(() => x.Documents.RemoveAsync(other.PublicId, Guid.NewGuid(), default));
    }

    // ================================================================ Lote 248: número, vigencia, edición y exención que respalda

    private static DateOnly Today => TenantClock.Default.Today;
    private const string IvuMarks = "{\"requiresValidTo\":true,\"isTax\":true}";
    private const string ResellerMarks = "{\"requiresValidTo\":false,\"isTax\":true}";

    /// <summary>Pone las marcas del tipo en el catálogo global (como la sección del lote en update.sql).</summary>
    private static async Task MarksAsync(SalesVendorWorld x, string code, string? json)
    {
        var l = await x.F.Db.LookupCodes.AsTracking().SingleAsync(r => r.Entity == ClientDocumentCodes.TypeDomain && r.InternalCode == code);
        l.ExtraJson = json;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    private static async Task<SalesVendorWorld> WorldAsync()
    {
        var x = await SalesVendorWorld.CreateAsync();
        foreach (var code in new[] { ClientDocumentCodes.IvuExemptionState, ClientDocumentCodes.IvuExemptionCity, ClientDocumentCodes.IvuExemptionTotal })
            await MarksAsync(x, code, IvuMarks);
        await MarksAsync(x, ClientDocumentCodes.ResellerCertificate, ResellerMarks);
        return x;
    }

    /// <summary>Una exención ACTIVA vinculada al documento (con la copia de su número y sus fechas).</summary>
    private static async Task<ClientTaxExemption> ExemptionAsync(SalesVendorWorld x, Guid documentPublicId)
    {
        var doc = await x.F.Db.Set<ClientDocument>().AsNoTracking().SingleAsync(d => d.PublicId == documentPublicId);
        var e = new ClientTaxExemption
        {
            PublicId = Guid.NewGuid(), TenantId = doc.TenantId, ClientId = doc.ClientId, ExemptionType = "Estatal", CertificateNumber = doc.DocumentNumber ?? "-",
            ValidFrom = doc.ValidFrom ?? Today, ValidTo = doc.ValidTo, IsActive = true, ClientDocumentId = doc.ClientDocumentId, CreatedAtUtc = DateTime.UtcNow,
        };
        x.F.Db.Set<ClientTaxExemption>().Add(e);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return e;
    }

    private static Task<ClientDocumentDto> UploadIvuAsync(SalesVendorWorld x, Guid client, DateOnly? validTo, string? number = "EX-1", DateOnly? validFrom = null,
        string type = ClientDocumentCodes.IvuExemptionState)
        => x.Documents.UploadAsync(client, type, null, "cert.png", "image/png", SalesVendorWorld.Png, default, number, validFrom, validTo);

    [Fact]
    public async Task Upload_with_number_and_dates_returns_days_and_state_and_validates_before_storing_the_file()
    {
        await using var x = await WorldAsync();
        var c = await x.ClientAsync("C1");

        var doc = await UploadIvuAsync(x, c.PublicId, Today.AddDays(10), " EX-778 ", Today.AddYears(-1));
        Assert.Equal(("EX-778", Today.AddYears(-1), Today.AddDays(10), 10, ClientDocumentExpiryStates.Expiring), (doc.DocumentNumber, doc.ValidFrom, doc.ValidTo, doc.DaysToExpiry, doc.ExpiryState));
        Assert.True(doc.IsTax && doc.RequiresValidTo && !doc.BacksExemption);
        Assert.True(doc.SuggestExemption is false);   // el módulo TAX está apagado en este mundo
        var far = await UploadIvuAsync(x, c.PublicId, Today.AddDays(200), type: ClientDocumentCodes.IvuExemptionCity);
        Assert.Equal(ClientDocumentExpiryStates.Valid, far.ExpiryState);
        // un «hasta» pasado se acepta: es el registro de un documento real
        var old = await UploadIvuAsync(x, c.PublicId, Today.AddDays(-3), type: ClientDocumentCodes.IvuExemptionTotal);
        Assert.Equal((ClientDocumentExpiryStates.Expired, -3), (old.ExpiryState, old.DaysToExpiry));

        var files = await x.F.Db.Set<StoredFile>().AsNoTracking().CountAsync();
        var noValidTo = await Assert.ThrowsAsync<ValidationException>(() => UploadIvuAsync(x, c.PublicId, null));
        Assert.Equal(ClientDocumentRules.ValidToRequired, noValidTo.Message);
        Assert.True(noValidTo.Errors!.ContainsKey("validTo"));
        var order = await Assert.ThrowsAsync<ValidationException>(() => UploadIvuAsync(x, c.PublicId, Today, validFrom: Today.AddDays(1)));
        Assert.Equal(ClientDocumentRules.DatesOrder, order.Message);
        var longNumber = await Assert.ThrowsAsync<ValidationException>(() => UploadIvuAsync(x, c.PublicId, Today, new string('9', 61)));
        Assert.Equal(ClientDocumentRules.NumberTooLong, longNumber.Message);
        Assert.Equal(files, await x.F.Db.Set<StoredFile>().AsNoTracking().CountAsync());   // nada llegó al almacén
    }

    [Fact]
    public async Task The_type_comes_from_the_catalog_and_a_type_turned_off_by_the_company_is_rejected()
    {
        await using var x = await WorldAsync();
        var c = await x.ClientAsync("C1");
        x.F.Db.LookupCodeOverrides.Add(new LookupCodeOverride
            { TenantId = WmsFixture.TenantId, LookupCodeId = x.Lookup(ClientDocumentCodes.TypeDomain, ClientDocumentCodes.IvuExemptionCity), IsEnabled = false });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var off = await Assert.ThrowsAsync<ValidationException>(() => UploadIvuAsync(x, c.PublicId, Today.AddDays(30), type: ClientDocumentCodes.IvuExemptionCity));
        Assert.Equal(ClientDocumentRules.TypeInvalid, off.Message);
        Assert.True(off.Errors!.ContainsKey("documentType"));
        // un tipo agregado en Catálogos de valores sirve aunque no esté en la lista sembrada
        x.F.Db.LookupCodes.Add(new LookupCode { LookupCodeId = 8800, Entity = ClientDocumentCodes.TypeDomain, InternalCode = "PERMISO_USO", LabelJson = "{\"es\":\"Permiso de uso\"}", IsActive = true });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var own = await x.Documents.UploadAsync(c.PublicId, "permiso_uso", null, "p.png", "image/png", SalesVendorWorld.Png, default);
        Assert.Equal(("PERMISO_USO", ClientDocumentExpiryStates.NoExpiry), (own.DocumentType, own.ExpiryState));
    }

    [Fact]
    public async Task Regression_app_movil_uploads_the_reseller_certificate_with_only_its_type_and_the_upload_suggests_the_exemption()
    {
        await using var x = await WorldAsync();
        var c = await x.ClientAsync("C1");
        // la firma del Lote 222, sin número ni fechas: el certificado de revendedor es de impuestos pero no lleva vigencia
        var doc = await x.Documents.UploadAsync(c.PublicId, ClientDocumentCodes.ResellerCertificate, null, "cert.png", "image/png", SalesVendorWorld.Png, default);
        Assert.Equal((ClientDocumentExpiryStates.NoExpiry, true, false, (DateOnly?)null), (doc.ExpiryState, doc.IsTax, doc.RequiresValidTo, doc.ValidTo));
        Assert.False(doc.SuggestExemption);   // TAX apagado

        x.F.SetModules(ModuleKeys.SalesOrders, ModuleKeys.Catalog, ModuleKeys.Billing, ModuleKeys.Tax);
        Assert.True((await UploadIvuAsync(x, c.PublicId, Today.AddDays(90))).SuggestExemption);   // administrador (tax.manage) con TAX encendido
        Assert.False((await x.Documents.UploadAsync(c.PublicId, ClientDocumentCodes.Other, null, "o.png", "image/png", SalesVendorWorld.Png, default)).SuggestExemption);
        x.F.SetPermissions(PermissionCatalog.ClientsUpdate, PermissionCatalog.ClientsRead);   // sin tax.manage
        Assert.False((await UploadIvuAsync(x, c.PublicId, Today.AddDays(90), type: ClientDocumentCodes.IvuExemptionTotal)).SuggestExemption);
    }

    [Fact]
    public async Task Patch_edits_number_dates_and_note_and_validates_the_final_state()
    {
        await using var x = await WorldAsync();
        var c = await x.ClientAsync("C1");
        var doc = await UploadIvuAsync(x, c.PublicId, Today.AddDays(40));

        var edited = await x.Documents.UpdateAsync(c.PublicId, doc.PublicId, new ClientDocumentPatchRequest(DocumentNumber: "EX-2", ValidTo: Today.AddDays(5), Notes: "Renovado"), default);
        Assert.Equal(("EX-2", Today.AddDays(5), "Renovado", ClientDocumentExpiryStates.Expiring), (edited.DocumentNumber, edited.ValidTo, edited.Notes, edited.ExpiryState));
        var cleared = await x.Documents.UpdateAsync(c.PublicId, doc.PublicId, new ClientDocumentPatchRequest(DocumentNumber: "", ClearValidFrom: true, Notes: ""), default);
        Assert.Equal(((string?)null, (DateOnly?)null, (string?)null, Today.AddDays(5)), (cleared.DocumentNumber, cleared.ValidFrom, cleared.Notes, cleared.ValidTo));

        var noValidTo = await Assert.ThrowsAsync<ValidationException>(() => x.Documents.UpdateAsync(c.PublicId, doc.PublicId, new ClientDocumentPatchRequest(ClearValidTo: true), default));
        Assert.Equal(ClientDocumentRules.ValidToRequired, noValidTo.Message);
        var order = await Assert.ThrowsAsync<ValidationException>(() => x.Documents.UpdateAsync(c.PublicId, doc.PublicId, new ClientDocumentPatchRequest(ValidFrom: Today.AddDays(9)), default));
        Assert.Equal(ClientDocumentRules.DatesOrder, order.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => x.Documents.UpdateAsync(c.PublicId, Guid.NewGuid(), new ClientDocumentPatchRequest(Notes: "x"), default));
        var other = await x.ClientAsync("C2");
        await Assert.ThrowsAsync<NotFoundException>(() => x.Documents.UpdateAsync(other.PublicId, doc.PublicId, new ClientDocumentPatchRequest(Notes: "x"), default));
    }

    [Fact]
    public async Task An_old_document_of_a_type_that_now_requires_validity_must_get_its_valid_to_even_to_change_the_note()
    {
        await using var x = await WorldAsync();
        var c = await x.ClientAsync("C1");
        var doc = await x.Documents.UploadAsync(c.PublicId, ClientDocumentCodes.Other, null, "o.png", "image/png", SalesVendorWorld.Png, default);
        await MarksAsync(x, ClientDocumentCodes.Other, "{\"requiresValidTo\":true}");

        Assert.Equal(ClientDocumentExpiryStates.MissingValidTo, (await x.Documents.ListAsync(c.PublicId, default)).Single().ExpiryState);   // no se bloquea ni se toca
        var ex = await Assert.ThrowsAsync<ValidationException>(() => x.Documents.UpdateAsync(c.PublicId, doc.PublicId, new ClientDocumentPatchRequest(Notes: "solo la nota"), default));
        Assert.Equal(ClientDocumentRules.ValidToRequired, ex.Message);
        var ok = await x.Documents.UpdateAsync(c.PublicId, doc.PublicId, new ClientDocumentPatchRequest(ValidTo: Today.AddDays(100), Notes: "solo la nota"), default);
        Assert.Equal(ClientDocumentExpiryStates.Valid, ok.ExpiryState);
    }

    [Fact]
    public async Task A_document_backing_an_exemption_needs_tax_manage_to_change_its_validity_and_the_exemption_copy_follows()
    {
        await using var x = await WorldAsync();
        var c = await x.ClientAsync("C1");
        var doc = await UploadIvuAsync(x, c.PublicId, Today.AddDays(20), "EX-1", Today.AddYears(-1));
        var exemption = await ExemptionAsync(x, doc.PublicId);
        Assert.True((await x.Documents.ListAsync(c.PublicId, default)).Single().BacksExemption);

        x.F.SetPermissions(PermissionCatalog.ClientsUpdate, PermissionCatalog.ClientsRead);
        var forbidden = await Assert.ThrowsAsync<ForbiddenException>(() =>
            x.Documents.UpdateAsync(c.PublicId, doc.PublicId, new ClientDocumentPatchRequest(ValidTo: Today.AddDays(400)), default));
        Assert.Equal(ClientDocumentRules.BacksExemptionNeedsTax, forbidden.Message);
        // la nota sí se cambia sin tax.manage (no es dinero)
        Assert.Equal("Nota", (await x.Documents.UpdateAsync(c.PublicId, doc.PublicId, new ClientDocumentPatchRequest(Notes: "Nota"), default)).Notes);

        x.F.SetPermissions(PermissionCatalog.ClientsUpdate, PermissionCatalog.ClientsRead, PermissionCatalog.TaxManage);
        await x.Documents.UpdateAsync(c.PublicId, doc.PublicId, new ClientDocumentPatchRequest(DocumentNumber: "EX-9", ValidFrom: Today, ValidTo: Today.AddDays(400)), default);
        var copy = await x.F.Db.Set<ClientTaxExemption>().AsNoTracking().SingleAsync(e => e.ClientTaxExemptionId == exemption.ClientTaxExemptionId);
        Assert.Equal(("EX-9", Today, (DateOnly?)Today.AddDays(400)), (copy.CertificateNumber, copy.ValidFrom, copy.ValidTo));
    }

    [Fact]
    public async Task The_valid_to_of_a_document_backing_an_exemption_cannot_be_cleared()
    {
        await using var x = await WorldAsync();
        var c = await x.ClientAsync("C1");
        // el certificado de revendedor no lleva vigencia, pero si respalda una exención su «hasta» no se quita
        var doc = await x.Documents.UploadAsync(c.PublicId, ClientDocumentCodes.ResellerCertificate, null, "r.png", "image/png", SalesVendorWorld.Png, default,
            "R-1", Today.AddYears(-1), Today.AddDays(60));
        await ExemptionAsync(x, doc.PublicId);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => x.Documents.UpdateAsync(c.PublicId, doc.PublicId, new ClientDocumentPatchRequest(ClearValidTo: true), default));
        Assert.Equal(ClientDocumentRules.BacksExemptionNeedsValidTo, ex.Message);
    }

    [Fact]
    public async Task A_document_backing_a_current_exemption_cannot_be_removed_but_an_expired_one_can()
    {
        await using var x = await WorldAsync();
        var c = await x.ClientAsync("C1");
        var current = await UploadIvuAsync(x, c.PublicId, Today);   // vence hoy: sigue vigente
        await ExemptionAsync(x, current.PublicId);
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => x.Documents.RemoveAsync(c.PublicId, current.PublicId, default));
        Assert.Equal(ClientDocumentRules.BacksExemptionCannotRemove, conflict.Message);
        Assert.Single(await x.Documents.ListAsync(c.PublicId, default));

        var expired = await UploadIvuAsync(x, c.PublicId, Today.AddDays(-1), type: ClientDocumentCodes.IvuExemptionCity);
        await ExemptionAsync(x, expired.PublicId);
        await x.Documents.RemoveAsync(c.PublicId, expired.PublicId, default);
        Assert.Equal(new[] { current.PublicId }, (await x.Documents.ListAsync(c.PublicId, default)).Select(d => d.PublicId));
    }

    [Fact]
    public async Task A_newer_document_of_the_same_type_renews_the_old_one()
    {
        await using var x = await WorldAsync();
        var c = await x.ClientAsync("C1");
        var old = await UploadIvuAsync(x, c.PublicId, Today.AddDays(3));
        var renewal = await UploadIvuAsync(x, c.PublicId, Today.AddDays(365));
        var otherType = await UploadIvuAsync(x, c.PublicId, Today.AddDays(2), type: ClientDocumentCodes.IvuExemptionCity);
        var list = (await x.Documents.ListAsync(c.PublicId, default)).ToDictionary(d => d.PublicId, d => d.ExpiryState);
        Assert.Equal(ClientDocumentExpiryStates.Renewed, list[old.PublicId]);
        Assert.Equal(ClientDocumentExpiryStates.Valid, list[renewal.PublicId]);
        Assert.Equal(ClientDocumentExpiryStates.Expiring, list[otherType.PublicId]);   // otro tipo no renueva
    }

    [Fact]
    public async Task A_prospect_seller_uploads_and_edits_but_an_active_client_needs_clients_update()
    {
        await using var x = await WorldAsync();
        var prospect = await x.Prospects.CreateBasicAsync(SalesVendorWorld.BasicRequest(), default);
        var active = await x.ClientAsync("C1");
        var activeDoc = await UploadIvuAsync(x, active.PublicId, Today.AddDays(50));
        x.F.SetPermissions(PermissionCatalog.ClientsCreateBasic, PermissionCatalog.ClientsRead);

        var doc = await UploadIvuAsync(x, prospect.PublicId, Today.AddDays(50));
        var edited = await x.Documents.UpdateAsync(prospect.PublicId, doc.PublicId, new ClientDocumentPatchRequest(DocumentNumber: "EX-77"), default);
        Assert.Equal("EX-77", edited.DocumentNumber);
        var forbidden = await Assert.ThrowsAsync<ForbiddenException>(() =>
            x.Documents.UpdateAsync(active.PublicId, activeDoc.PublicId, new ClientDocumentPatchRequest(DocumentNumber: "EX-78"), default));
        Assert.Equal(ClientProspectRules.DocumentNeedsPermission, forbidden.Message);
    }

    [Fact]
    public void Form_dates_are_iso_and_anything_else_is_a_400_on_its_field()
    {
        Assert.Null(ClientDocumentService.ParseFormDate(null, "validTo"));
        Assert.Null(ClientDocumentService.ParseFormDate("  ", "validTo"));
        Assert.Equal(new DateOnly(2026, 12, 31), ClientDocumentService.ParseFormDate(" 2026-12-31 ", "validTo"));
        var ex = Assert.Throws<ValidationException>(() => ClientDocumentService.ParseFormDate("31/12/2026", "validFrom"));
        Assert.Equal((ClientDocumentService.DateFormatInvalid, true), (ex.Message, ex.Errors!.ContainsKey("validFrom")));
    }
}
