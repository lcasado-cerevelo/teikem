using System.Text;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Infrastructure.Exceptions;
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
        Assert.Equal(ClientProspectRules.DocumentTypeInvalid, badType.Message);
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
}
